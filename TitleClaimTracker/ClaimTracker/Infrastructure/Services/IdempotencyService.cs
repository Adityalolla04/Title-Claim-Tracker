using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TitleClaimTracker.Core.Exceptions;
using TitleClaimTracker.Domain.Entities;
using TitleClaimTracker.Infrastructure.Data;
using TitleClaimTracker.Infrastructure.Security;

namespace TitleClaimTracker.Infrastructure.Services;

public sealed record IdempotentExecution<T>(T Value, bool IsReplay, int StatusCode);

public interface IIdempotencyService
{
    Task<IdempotentExecution<T>> ExecuteAsync<T>(string operationName, string key, object payload, Func<CancellationToken, Task<T>> operation, int completedStatusCode, CancellationToken cancellationToken);
}

public sealed class IdempotencyService(TitleClaimDbContext db, ICurrentUserAccessor currentUser) : IIdempotencyService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IdempotentExecution<T>> ExecuteAsync<T>(string operationName, string key, object payload, Func<CancellationToken, Task<T>> operation, int completedStatusCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 200)
        {
            throw new ArgumentException("An Idempotency-Key header of 1 to 200 characters is required.", nameof(key));
        }

        var userId = currentUser.RequireUserId();
        var payloadHash = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOptions)));
        var existing = await db.IdempotencyRecords.SingleOrDefaultAsync(x => x.UserId == userId && x.OperationName == operationName && x.IdempotencyKey == key, cancellationToken);
        if (existing is not null)
        {
            if (!CryptographicOperations.FixedTimeEquals(existing.PayloadHash, payloadHash))
            {
                throw new IdempotencyConflictException("This idempotency key was already used with a different request.");
            }

            if (existing.State == "Completed" && existing.ResponseBody is not null)
            {
                return new IdempotentExecution<T>(
                    JsonSerializer.Deserialize<T>(existing.ResponseBody, JsonOptions) ?? throw new InvalidOperationException("Stored idempotency response is invalid."),
                    true,
                    existing.ResponseStatusCode ?? completedStatusCode);
            }

            if (existing.State == "Started" && existing.CreatedUtc > DateTime.UtcNow.AddMinutes(-10))
            {
                throw new IdempotencyConflictException("A matching request is already in progress.");
            }

            existing.State = "Started";
            existing.ResponseStatusCode = null;
            existing.ResponseBody = null;
            existing.CreatedUtc = DateTime.UtcNow;
            existing.CompletedUtc = null;
        }
        else
        {
            db.IdempotencyRecords.Add(new IdempotencyRecord { UserId = userId, OperationName = operationName, IdempotencyKey = key, PayloadHash = payloadHash, State = "Started" });
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var operationStarted = false;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            operationStarted = true;
            var value = await operation(cancellationToken);
            var record = existing ?? await db.IdempotencyRecords.SingleAsync(x => x.UserId == userId && x.OperationName == operationName && x.IdempotencyKey == key, cancellationToken);
            record.State = "Completed";
            record.ResponseStatusCode = completedStatusCode;
            record.ResponseBody = JsonSerializer.Serialize(value, JsonOptions);
            record.CompletedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new IdempotentExecution<T>(value, false, completedStatusCode);
        }
        catch (DbUpdateException) when (!operationStarted)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new IdempotencyConflictException("The idempotency request could not be reserved. Retry with the same key.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
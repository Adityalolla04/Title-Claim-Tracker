using Microsoft.EntityFrameworkCore;
using TitleClaimTracker.Core.DTOs;
using TitleClaimTracker.Core.Exceptions;
using TitleClaimTracker.Domain.Entities;
using TitleClaimTracker.Infrastructure.Data;
using TitleClaimTracker.Infrastructure.Repositories;

namespace TitleClaimTracker.Infrastructure.Services;

public sealed class FilingService(TitleClaimDbContext db, IFilingRepository repository, ILogger<FilingService> logger) : IFilingService
{
    private static readonly IReadOnlyDictionary<string, string[]> AllowedTransitions = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { ["New"] = ["Under Review", "Escalated", "Closed"], ["Under Review"] = ["Escalated", "Resolved", "Closed"], ["Escalated"] = ["Under Review", "Resolved", "Closed"], ["Resolved"] = [], ["Closed"] = [] };
    public async Task<FilingSummaryDto> CreateAsync(CreateFilingDto request, CancellationToken cancellationToken)
    {
        if (!LegalFiling.ValidStatuses.Contains("New")) throw new InvalidOperationException("Status configuration is invalid.");
        var type = await db.FilingTypes.SingleOrDefaultAsync(x => x.TypeName == request.FilingType, cancellationToken) ?? throw new KeyNotFoundException($"Unknown filing type '{request.FilingType}'.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var filing = new LegalFiling { Property = new Property { Address = request.Address.Trim(), City = request.City.Trim(), State = request.State.Trim().ToUpperInvariant(), LegalDescription = request.LegalDescription, CreatedDate = DateTime.UtcNow }, FilingTypeID = type.FilingTypeID, DateFiled = request.DateFiled.Date, ClaimantName = request.ClaimantName, Notes = request.Notes, RiskScore = request.RiskScore, Status = "New" };
        await repository.AddAsync(filing, cancellationToken); await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); logger.LogInformation("Created filing {FilingId}", filing.FilingID); return Map(filing);
    }
    public async Task<FilingSummaryDto> GetAsync(int id, CancellationToken cancellationToken) => Map(await repository.GetByIdAsync(id, cancellationToken) ?? throw new KeyNotFoundException($"Filing {id} was not found."));
    public async Task<(IReadOnlyList<FilingSummaryDto> Items, int Total)> SearchAsync(string? status, string? filingType, string? city, int page, int pageSize, CancellationToken cancellationToken) { page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100); var result = await repository.SearchAsync(status, filingType, city, page, pageSize, cancellationToken); return (result.Items.Select(Map).ToList(), result.Total); }
    public async Task<FilingSummaryDto> UpdateStatusAsync(int id, string status, CancellationToken cancellationToken)
    {
        if (!LegalFiling.ValidStatuses.Contains(status, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException($"Invalid status '{status}'.", nameof(status));
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var filing = await repository.GetByIdAsync(id, cancellationToken) ?? throw new KeyNotFoundException($"Filing {id} was not found.");
        if (!AllowedTransitions[filing.Status].Contains(status, StringComparer.OrdinalIgnoreCase) && !string.Equals(filing.Status, status, StringComparison.OrdinalIgnoreCase)) throw new InvalidFilingStatusTransitionException(filing.Status, status);
        filing.Status = status; await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return Map(filing);
    }
    public async Task DeleteAsync(int id, CancellationToken cancellationToken) { var filing = await repository.GetByIdAsync(id, cancellationToken) ?? throw new KeyNotFoundException($"Filing {id} was not found."); repository.Remove(filing); await db.SaveChangesAsync(cancellationToken); }
    private static FilingSummaryDto Map(LegalFiling x) => new(x.FilingID, x.PropertyID, x.Property?.Address ?? "", x.Property?.City ?? "", x.Property?.State ?? "", x.FilingType?.TypeName ?? "", x.DateFiled, x.Status, x.ClaimantName, x.Notes, x.RiskScore);
}

using Microsoft.EntityFrameworkCore;
using TitleClaimTracker.Core.DTOs;
using TitleClaimTracker.Core.Exceptions;
using TitleClaimTracker.Domain.Entities;
using TitleClaimTracker.Infrastructure.Data;
using TitleClaimTracker.Infrastructure.Repositories;
using TitleClaimTracker.Infrastructure.Security;

namespace TitleClaimTracker.Infrastructure.Services;

public sealed class FilingService(
    TitleClaimDbContext db,
    IFilingRepository repository,
    ICurrentUserAccessor currentUser,
    ILogger<FilingService> logger) : IFilingService
{
    private static readonly IReadOnlyDictionary<string, string[]> AllowedTransitions = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["New"] = ["Under Review", "Escalated", "Closed"],
        ["Under Review"] = ["Escalated", "Resolved", "Closed"],
        ["Escalated"] = ["Under Review", "Resolved", "Closed"],
        ["Resolved"] = ["Closed"],
        ["Pending Review"] = ["Under Review", "Escalated", "Resolved", "Closed"],
        ["Active Investigation"] = ["Under Review", "Escalated", "Resolved", "Closed"],
        ["Closed"] = []
    };

    public async Task<ClaimSummaryDto> CreateAsync(
        CreateClaimDto request,
        CancellationToken cancellationToken,
        string creationSource = "Manual",
        decimal? classificationScore = null,
        byte? triagePriority = null,
        string? triageRuleVersion = null,
        long? modelVersionId = null)
    {
        var userId = currentUser.RequireUserId();
        if (creationSource is not ("Manual" or "Assisted"))
        {
            throw new ArgumentException($"Invalid creation source '{creationSource}'.", nameof(creationSource));
        }

        if (classificationScore is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(classificationScore));
        }

        var type = await db.FilingTypes.SingleOrDefaultAsync(x => x.TypeName == request.FilingType, cancellationToken)
            ?? throw new KeyNotFoundException($"Unknown filing type '{request.FilingType}'.");
        var ownsTransaction = db.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        var address = request.Address.Trim();
        var city = request.City.Trim();
        var state = request.State.Trim().ToUpperInvariant();
        var property = await db.Properties.SingleOrDefaultAsync(x => x.Address == address && x.City == city && x.State == state, cancellationToken);
        if (property is null)
        {
            property = new Property { Address = address, City = city, State = state, LegalDescription = request.LegalDescription?.Trim() };
        }

        var filing = new LegalFiling
        {
            Property = property,
            FilingTypeID = type.FilingTypeID,
            DateFiled = request.DateFiled.Date,
            ClaimantName = request.ClaimantName?.Trim(),
            Notes = request.Notes?.Trim(),
            RiskScore = creationSource == "Assisted" ? request.RiskScore : null,
            SubmittedByUserId = userId,
            CreationSource = creationSource,
            ClassificationScore = classificationScore,
            TriagePriority = triagePriority ?? (creationSource == "Assisted" ? CalculatePriority(request.RiskScore) : (byte)3),
            TriageRuleVersion = triageRuleVersion ?? (creationSource == "Assisted" ? "triage-v1" : "manual"),
            ModelVersionID = modelVersionId,
            Status = "New"
        };
        await repository.AddAsync(filing, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        db.ClaimStatusHistory.Add(new ClaimStatusHistory
        {
            FilingID = filing.FilingID,
            ToStatus = filing.Status,
            ActorUserId = userId,
            TransitionedUtc = DateTime.UtcNow,
            Reason = "Claim created"
        });
        WriteAudit(filing.FilingID, userId, "Insert", "Claim", null, "New");
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Created filing {FilingId} for account {UserId} using {CreationSource} intake.", filing.FilingID, userId, creationSource);
        return Map(filing);
    }

    public async Task<ClaimDetailDto> GetAsync(int id, CancellationToken cancellationToken)
    {
        var filing = await repository.GetByIdAsync(id, currentUser.RequireUserId(), currentUser.IsAdministrator, cancellationToken)
            ?? throw new KeyNotFoundException($"Filing {id} was not found.");
        return MapDetail(filing);
    }

    public async Task<(IReadOnlyList<ClaimSummaryDto> Items, int Total)> SearchAsync(string? status, string? filingType, string? city, int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var result = await repository.SearchAsync(NormalizeFilter(status), NormalizeFilter(filingType), NormalizeFilter(city), page, pageSize, currentUser.RequireUserId(), currentUser.IsAdministrator, cancellationToken);
        return (result.Items.Select(Map).ToList(), result.Total);
    }

    public Task<ClaimSummaryDto> UpdateStatusAsync(int id, string status, CancellationToken cancellationToken) =>
        UpdateStatusAsync(id, new UpdateClaimStatusDto(status, string.Empty), cancellationToken);

    public async Task<ClaimSummaryDto> UpdateStatusAsync(int id, UpdateClaimStatusDto request, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAdministrator)
        {
            throw new UnauthorizedAccessException("Only administrators can change filing status.");
        }

        var canonicalStatus = LegalFiling.ValidStatuses.FirstOrDefault(x => string.Equals(x, request.Status, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Invalid status '{request.Status}'.", nameof(request));
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var filing = await repository.GetByIdAsync(id, currentUser.RequireUserId(), isAdministrator: true, cancellationToken)
            ?? throw new KeyNotFoundException($"Filing {id} was not found.");
        if (!AllowedTransitions.TryGetValue(filing.Status, out var allowedStatuses) ||
            (!allowedStatuses.Contains(canonicalStatus, StringComparer.OrdinalIgnoreCase) && !string.Equals(filing.Status, canonicalStatus, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidFilingStatusTransitionException(filing.Status, canonicalStatus);
        }

        ApplyRowVersion(filing, request.RowVersion);
        var previousStatus = filing.Status;
        filing.Status = canonicalStatus;
        filing.UpdatedUtc = DateTime.UtcNow;
        db.ClaimStatusHistory.Add(new ClaimStatusHistory
        {
            FilingID = filing.FilingID,
            FromStatus = previousStatus,
            ToStatus = canonicalStatus,
            ActorUserId = currentUser.RequireUserId(),
            TransitionedUtc = DateTime.UtcNow,
            Reason = request.Reason?.Trim()
        });
        WriteAudit(filing.FilingID, currentUser.RequireUserId(), "StatusTransition", "Status", previousStatus, canonicalStatus);
        await SaveWithConcurrencyAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Map(filing);
    }

    public async Task<ClaimDetailDto> UpdateAsync(int id, UpdateClaimDto request, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAdministrator)
        {
            throw new UnauthorizedAccessException("Only administrators can edit filings.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var filing = await repository.GetByIdAsync(id, currentUser.RequireUserId(), true, cancellationToken)
            ?? throw new KeyNotFoundException($"Filing {id} was not found.");
        ApplyRowVersion(filing, request.RowVersion);
        var type = await db.FilingTypes.SingleOrDefaultAsync(x => x.TypeName == request.FilingType, cancellationToken)
            ?? throw new ArgumentException("The filing type is unknown.", nameof(request));
        var property = await db.Properties.SingleOrDefaultAsync(x => x.Address == request.Address.Trim() && x.City == request.City.Trim() && x.State == request.State.Trim().ToUpperInvariant(), cancellationToken);
        if (property is null)
        {
            property = new Property { Address = request.Address.Trim(), City = request.City.Trim(), State = request.State.Trim().ToUpperInvariant(), LegalDescription = request.LegalDescription?.Trim() };
            db.Properties.Add(property);
        }

        var previous = $"{filing.Property?.Address}, {filing.Property?.City}, {filing.Property?.State}";
        filing.Property = property;
        filing.FilingTypeID = type.FilingTypeID;
        filing.DateFiled = request.DateFiled.Date;
        filing.ClaimantName = request.ClaimantName?.Trim();
        filing.Notes = request.Notes?.Trim();
        filing.UpdatedUtc = DateTime.UtcNow;
        WriteAudit(filing.FilingID, currentUser.RequireUserId(), "Update", "Claim", previous, $"{property.Address}, {property.City}, {property.State}");
        await SaveWithConcurrencyAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return MapDetail(filing);
    }

    public async Task DeleteAsync(int id, string rowVersion, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAdministrator)
        {
            throw new UnauthorizedAccessException("Only administrators can delete filings.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var filing = await repository.GetByIdAsync(id, currentUser.RequireUserId(), isAdministrator: true, cancellationToken)
            ?? throw new KeyNotFoundException($"Filing {id} was not found.");
        ApplyRowVersion(filing, rowVersion);
        filing.IsDeleted = true;
        filing.DeletedUtc = DateTime.UtcNow;
        filing.DeletedByUserId = currentUser.RequireUserId();
        filing.UpdatedUtc = DateTime.UtcNow;
        WriteAudit(filing.FilingID, currentUser.RequireUserId(), "SoftDelete", "IsDeleted", "false", "true");
        await SaveWithConcurrencyAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static byte CalculatePriority(double? riskScore) => riskScore switch
    {
        >= .8 => 1,
        >= .6 => 2,
        >= .4 => 3,
        >= .2 => 4,
        _ => 5
    };

    private static string? NormalizeFilter(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ClaimSummaryDto Map(LegalFiling x) => new(
        x.FilingID,
        x.PropertyID,
        x.Property?.Address ?? string.Empty,
        x.Property?.City ?? string.Empty,
        x.Property?.State ?? string.Empty,
        x.FilingType?.TypeName ?? string.Empty,
        x.DateFiled,
        x.Status,
        x.ClaimantName,
        x.Notes,
        x.RiskScore);

    private static ClaimDetailDto MapDetail(LegalFiling x) => new(
        x.FilingID, x.PropertyID, x.Property?.Address ?? string.Empty, x.Property?.City ?? string.Empty,
        x.Property?.State ?? string.Empty, x.Property?.LegalDescription, x.FilingType?.TypeName ?? string.Empty,
        x.DateFiled, x.Status, x.ClaimantName, x.Notes, x.RiskScore, x.CreationSource,
        x.ClassificationScore, x.TriagePriority, Convert.ToBase64String(x.RowVersion),
        x.StatusHistory.OrderByDescending(history => history.TransitionedUtc).ThenByDescending(history => history.StatusHistoryID)
            .Select(history => new ClaimStatusHistoryDto(history.StatusHistoryID, history.FromStatus, history.ToStatus, history.TransitionedUtc, history.Reason)).ToArray());

    private void WriteAudit(int filingId, string userId, string action, string column, string? oldValue, string? newValue) =>
        db.AuditLogs.Add(new AuditLog
        {
            TableName = "LegalFilings",
            RecordID = filingId,
            Action = action,
            ColumnChanged = column,
            OldValue = oldValue,
            NewValue = newValue,
            ActorUserId = userId,
            ChangeDate = DateTime.UtcNow
        });

    private void ApplyRowVersion(LegalFiling filing, string rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion))
        {
            throw new ArgumentException("A row version is required for this mutation.", nameof(rowVersion));
        }

        try
        {
            db.Entry(filing).Property(x => x.RowVersion).OriginalValue = Convert.FromBase64String(rowVersion);
        }
        catch (FormatException)
        {
            throw new ArgumentException("The row version is invalid.", nameof(rowVersion));
        }
    }

    private async Task SaveWithConcurrencyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException();
        }
    }
}
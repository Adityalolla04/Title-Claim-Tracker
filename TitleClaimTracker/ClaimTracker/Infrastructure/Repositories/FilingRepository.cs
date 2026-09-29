using Microsoft.EntityFrameworkCore;
using TitleClaimTracker.Core.DTOs;
using TitleClaimTracker.Domain.Entities;
using TitleClaimTracker.Infrastructure.Data;

namespace TitleClaimTracker.Infrastructure.Repositories;

public sealed class FilingRepository(TitleClaimDbContext db) : IFilingRepository
{
    public Task<LegalFiling?> GetByIdAsync(int id, string userId, bool isAdministrator, CancellationToken cancellationToken) =>
        QueryForUser(userId, isAdministrator).Include(x => x.Property).Include(x => x.FilingType).Include(x => x.StatusHistory).SingleOrDefaultAsync(x => x.FilingID == id, cancellationToken);

    public async Task<(IReadOnlyList<LegalFiling> Items, int Total)> SearchAsync(string? status, string? filingType, string? city, string? search, int page, int pageSize, string userId, bool isAdministrator, CancellationToken cancellationToken)
    {
        var query = QueryForUser(userId, isAdministrator).AsNoTracking().Include(x => x.Property).Include(x => x.FilingType).AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(filingType)) query = query.Where(x => x.FilingType!.TypeName == filingType);
        if (!string.IsNullOrWhiteSpace(city)) query = query.Where(x => x.Property!.City == city);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().TrimStart('#');
            var hasFilingId = int.TryParse(term, out var filingId);
            var normalizedTerm = term.ToLowerInvariant();
            query = query.Where(x => (hasFilingId && x.FilingID == filingId) || (x.ClaimantName != null && x.ClaimantName.ToLower().Contains(normalizedTerm)));
        }
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.DateFiled).ThenByDescending(x => x.FilingID).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<ClaimAnalyticsDto> GetAnalyticsAsync(string userId, bool isAdministrator, CancellationToken cancellationToken)
    {
        var query = QueryForUser(userId, isAdministrator).AsNoTracking();
        var total = await query.CountAsync(cancellationToken);
        var open = await query.CountAsync(x => x.Status != "Resolved" && x.Status != "Closed", cancellationToken);
        var statusRows = await query.GroupBy(x => x.Status)
            .Select(group => new { Label = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var statuses = statusRows.OrderBy(item => item.Label)
            .Select(item => new ClaimCountDto(item.Label, item.Count)).ToArray();
        var typeRows = await query.GroupBy(x => x.FilingType!.TypeName)
            .Select(group => new { Label = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var types = typeRows.OrderBy(item => item.Label)
            .Select(item => new ClaimCountDto(item.Label, item.Count)).ToArray();
        var now = DateTime.UtcNow;
        var firstMonth = new DateTime(now.Year, now.Month, 1).AddMonths(-5);
        var recentDates = await query.Where(x => x.DateFiled >= firstMonth).Select(x => x.DateFiled).ToListAsync(cancellationToken);
        var firstSolvedDates = await db.ClaimStatusHistory.AsNoTracking()
            .Where(history => (history.ToStatus == "Resolved" || history.ToStatus == "Closed") &&
                history.Filing != null && !history.Filing.IsDeleted &&
                (isAdministrator || history.Filing.SubmittedByUserId == userId))
            .GroupBy(history => history.FilingID)
            .Select(group => group.Min(history => history.TransitionedUtc))
            .ToListAsync(cancellationToken);
        var recentSolvedDates = firstSolvedDates.Where(date => date >= firstMonth).ToArray();
        var months = Enumerable.Range(0, 6).Select(offset => firstMonth.AddMonths(offset))
            .Select(month => new MonthlyClaimCountDto(
                month.Year,
                month.Month,
                recentDates.Count(date => date.Year == month.Year && date.Month == month.Month),
                recentSolvedDates.Count(date => date.Year == month.Year && date.Month == month.Month)))
            .ToArray();
        return new ClaimAnalyticsDto(total, open, total - open, statuses, types, months);
    }

    public Task AddAsync(LegalFiling filing, CancellationToken cancellationToken) => db.LegalFilings.AddAsync(filing, cancellationToken).AsTask();

    private IQueryable<LegalFiling> QueryForUser(string userId, bool isAdministrator) =>
        db.LegalFilings.Where(x => !x.IsDeleted && (isAdministrator || x.SubmittedByUserId == userId));
}

using Microsoft.EntityFrameworkCore;
using TitleClaimTracker.Domain.Entities;
using TitleClaimTracker.Infrastructure.Data;

namespace TitleClaimTracker.Infrastructure.Repositories;

public sealed class FilingRepository(TitleClaimDbContext db) : IFilingRepository
{
    public Task<LegalFiling?> GetByIdAsync(int id, string userId, bool isAdministrator, CancellationToken cancellationToken) =>
        QueryForUser(userId, isAdministrator).Include(x => x.Property).Include(x => x.FilingType).Include(x => x.StatusHistory).SingleOrDefaultAsync(x => x.FilingID == id, cancellationToken);

    public async Task<(IReadOnlyList<LegalFiling> Items, int Total)> SearchAsync(string? status, string? filingType, string? city, int page, int pageSize, string userId, bool isAdministrator, CancellationToken cancellationToken)
    {
        var query = QueryForUser(userId, isAdministrator).AsNoTracking().Include(x => x.Property).Include(x => x.FilingType).AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(filingType)) query = query.Where(x => x.FilingType!.TypeName == filingType);
        if (!string.IsNullOrWhiteSpace(city)) query = query.Where(x => x.Property!.City == city);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.DateFiled).ThenByDescending(x => x.FilingID).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return (items, total);
    }
    public Task AddAsync(LegalFiling filing, CancellationToken cancellationToken) => db.LegalFilings.AddAsync(filing, cancellationToken).AsTask();

    private IQueryable<LegalFiling> QueryForUser(string userId, bool isAdministrator) =>
        db.LegalFilings.Where(x => !x.IsDeleted && (isAdministrator || x.SubmittedByUserId == userId));
}

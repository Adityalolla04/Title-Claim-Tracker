using TitleClaimTracker.Domain.Entities;

namespace TitleClaimTracker.Infrastructure.Repositories;

public interface IFilingRepository
{
    Task<LegalFiling?> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<(IReadOnlyList<LegalFiling> Items, int Total)> SearchAsync(string? status, string? filingType, string? city, int page, int pageSize, CancellationToken cancellationToken);
    Task AddAsync(LegalFiling filing, CancellationToken cancellationToken);
    void Remove(LegalFiling filing);
}

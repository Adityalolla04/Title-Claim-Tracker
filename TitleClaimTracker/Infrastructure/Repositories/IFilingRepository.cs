using TitleClaimTracker.Domain.Entities;

namespace TitleClaimTracker.Infrastructure.Repositories;

public interface IFilingRepository
{
    Task<LegalFiling?> GetByIdAsync(int id, string userId, bool isAdministrator, CancellationToken cancellationToken);
    Task<(IReadOnlyList<LegalFiling> Items, int Total)> SearchAsync(string? status, string? filingType, string? city, int page, int pageSize, string userId, bool isAdministrator, CancellationToken cancellationToken);
    Task AddAsync(LegalFiling filing, CancellationToken cancellationToken);
}

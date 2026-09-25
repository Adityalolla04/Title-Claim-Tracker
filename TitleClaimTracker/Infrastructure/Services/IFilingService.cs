using TitleClaimTracker.Core.DTOs;

namespace TitleClaimTracker.Infrastructure.Services;

public interface IFilingService
{
    Task<ClaimSummaryDto> CreateAsync(CreateClaimDto request, CancellationToken cancellationToken, string creationSource = "Manual", decimal? classificationScore = null, byte? triagePriority = null, string? triageRuleVersion = null, long? modelVersionId = null);
    Task<ClaimDetailDto> GetAsync(int id, CancellationToken cancellationToken);
    Task<(IReadOnlyList<ClaimSummaryDto> Items, int Total)> SearchAsync(string? status, string? filingType, string? city, int page, int pageSize, CancellationToken cancellationToken);
    Task<ClaimDetailDto> UpdateAsync(int id, UpdateClaimDto request, CancellationToken cancellationToken);
    Task<ClaimSummaryDto> UpdateStatusAsync(int id, string status, CancellationToken cancellationToken);
    Task<ClaimSummaryDto> UpdateStatusAsync(int id, UpdateClaimStatusDto request, CancellationToken cancellationToken);
    Task DeleteAsync(int id, string rowVersion, CancellationToken cancellationToken);
}

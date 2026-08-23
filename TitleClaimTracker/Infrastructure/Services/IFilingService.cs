using TitleClaimTracker.Core.DTOs;

namespace TitleClaimTracker.Infrastructure.Services;

public interface IFilingService
{
    Task<FilingSummaryDto> CreateAsync(CreateFilingDto request, CancellationToken cancellationToken);
    Task<FilingSummaryDto> GetAsync(int id, CancellationToken cancellationToken);
    Task<(IReadOnlyList<FilingSummaryDto> Items, int Total)> SearchAsync(string? status, string? filingType, string? city, int page, int pageSize, CancellationToken cancellationToken);
    Task<FilingSummaryDto> UpdateStatusAsync(int id, string status, CancellationToken cancellationToken);
    Task DeleteAsync(int id, CancellationToken cancellationToken);
}

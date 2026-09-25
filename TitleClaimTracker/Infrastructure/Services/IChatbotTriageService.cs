// File: TitleClaimTracker/Infrastructure/Services/IChatbotTriageService.cs
using TitleClaimTracker.Core.DTOs;

namespace TitleClaimTracker.Infrastructure.Services;

public interface IChatbotTriageService
{
    Task<ChatbotResponseDto> TriageAsync(ChatTriageRequestDto request, CancellationToken cancellationToken);
    Task<ClaimSummaryDto> SubmitDraftAsync(SubmitTriageDraftDto request, CancellationToken cancellationToken);
    Task<IReadOnlyList<TriageAttemptDto>> GetReviewQueueAsync(CancellationToken cancellationToken);
    Task RecordFeedbackAsync(long attemptId, TriageFeedbackDto request, CancellationToken cancellationToken);
}

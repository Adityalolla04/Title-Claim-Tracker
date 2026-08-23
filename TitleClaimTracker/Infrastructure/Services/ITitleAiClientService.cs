using TitleClaimTracker.Core.DTOs;

namespace TitleClaimTracker.Infrastructure.Services;

public interface ITitleAiClientService
{
    Task<AiExtractedClaimResponseDto> ExtractClaimAsync(string rawText, CancellationToken cancellationToken);
}

using System.Net.Http.Json;
using TitleClaimTracker.Core.DTOs;

namespace TitleClaimTracker.Infrastructure.Services;

public sealed class TitleAiClientService(HttpClient httpClient, ILogger<TitleAiClientService> logger) : ITitleAiClientService
{
    public async Task<AiExtractedClaimResponseDto> ExtractClaimAsync(string rawText, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync("api/ai/extract-claim", new { raw_text = rawText }, cancellationToken);
            if (!response.IsSuccessStatusCode) { logger.LogWarning("AI service returned {StatusCode}", response.StatusCode); throw new HttpRequestException($"AI service returned {(int)response.StatusCode}."); }
            return await response.Content.ReadFromJsonAsync<AiExtractedClaimResponseDto>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("AI service returned an empty response.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new TimeoutException("The AI service request timed out."); }
    }
}

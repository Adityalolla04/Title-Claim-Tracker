using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TitleClaimTracker.Core.DTOs;
using TitleClaimTracker.Core.Exceptions;
using TitleClaimTracker.Infrastructure.Services;

namespace TitleClaimTracker.Controllers;

public sealed class AiServiceOptions { public double MinimumConfidence { get; set; } = 0.65; }

[ApiController, Route("api/chatbot")]
public sealed class ClaimsChatbotController(ITitleAiClientService aiClient, IFilingService filingService, IOptions<AiServiceOptions> options) : ControllerBase
{
    [HttpPost("extract")]
    public async Task<ActionResult<ChatbotResponseDto>> Extract(AiTriageRequestDto request, CancellationToken cancellationToken)
    {
        var extracted = await aiClient.ExtractClaimAsync(request.RawText, cancellationToken);
        if (extracted.ConfidenceScore < options.Value.MinimumConfidence) throw new AiExtractionConfidenceException(extracted.ConfidenceScore, options.Value.MinimumConfidence);
        return Ok(new ChatbotResponseDto("I extracted the claim details below. Confirm them to save the filing.", true, extracted, null));
    }

    [HttpPost("confirm")]
    public async Task<ActionResult<ChatbotResponseDto>> Confirm(CreateFilingDto request, CancellationToken cancellationToken)
    {
        var saved = await filingService.CreateAsync(request, cancellationToken);
        return Ok(new ChatbotResponseDto($"Filing {saved.FilingID} was saved successfully.", false, null, saved));
    }
}

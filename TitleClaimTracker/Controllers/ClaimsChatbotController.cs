// File: TitleClaimTracker/Controllers/ClaimsChatbotController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TitleClaimTracker.Core.DTOs;
using TitleClaimTracker.Infrastructure.Services;

namespace TitleClaimTracker.Controllers;

[ApiController, Authorize, Route("api/chatbot")]
public sealed class ClaimsChatbotController(IChatbotTriageService triageService) : ControllerBase
{
    [HttpPost("triage")]
    public async Task<ActionResult<ChatbotResponseDto>> Triage([FromBody] ChatTriageRequestDto request, CancellationToken cancellationToken) => Ok(await triageService.TriageAsync(request, cancellationToken));

    [HttpPost("drafts/submit")]
    public async Task<ActionResult<ClaimSummaryDto>> SubmitDraft([FromBody] SubmitTriageDraftDto request, [FromServices] IIdempotencyService idempotency, CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue("Idempotency-Key", out var key)) return BadRequest(new { error = "An Idempotency-Key header is required." });
        var result = await idempotency.ExecuteAsync("triage.submit", key.ToString(), request, token => triageService.SubmitDraftAsync(request, token), StatusCodes.Status200OK, cancellationToken);
        return StatusCode(result.StatusCode, result.Value);
    }

    [Authorize(Roles = "Admin"), HttpGet("review")]
    public async Task<ActionResult<IReadOnlyList<TriageAttemptDto>>> Review(CancellationToken cancellationToken) => Ok(await triageService.GetReviewQueueAsync(cancellationToken));

    [Authorize(Roles = "Admin"), HttpPost("review/{attemptId:long}/feedback")]
    public async Task<IActionResult> Feedback(long attemptId, [FromBody] TriageFeedbackDto request, CancellationToken cancellationToken)
    {
        await triageService.RecordFeedbackAsync(attemptId, request, cancellationToken);
        return NoContent();
    }
}
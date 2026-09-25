// File: TitleClaimTracker/Controllers/FilingsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using TitleClaimTracker.Core.DTOs;
using TitleClaimTracker.Infrastructure.Data;
using TitleClaimTracker.Infrastructure.Services;

namespace TitleClaimTracker.Controllers;

[ApiController, Authorize, Route("api/filings")]
public sealed class FilingsController(IFilingService service, TitleClaimDbContext db) : ControllerBase
{
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ClaimDetailDto>> Get(int id, CancellationToken cancellationToken) => Ok(await service.GetAsync(id, cancellationToken));

    [HttpGet]
    public async Task<ActionResult<PagedClaimsDto>> Search([FromQuery] string? status, [FromQuery] string? filingType, [FromQuery] string? city, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize is < 1 or > 100)
        {
            return BadRequest(new { error = "Page must be at least 1 and pageSize must be between 1 and 100." });
        }

        var result = await service.SearchAsync(status, filingType, city, page, pageSize, cancellationToken);
        return Ok(new PagedClaimsDto(result.Items, result.Total, page, pageSize));
    }

    [HttpPost]
    public async Task<ActionResult<ClaimSummaryDto>> Create([FromBody] CreateClaimDto request, [FromServices] IIdempotencyService idempotency, CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue("Idempotency-Key", out var key))
        {
            return BadRequest(new { error = "An Idempotency-Key header is required." });
        }

        var result = await idempotency.ExecuteAsync("claim.create", key.ToString(), request, token => service.CreateAsync(request, token), StatusCodes.Status201Created, cancellationToken);
        return result.IsReplay ? StatusCode(result.StatusCode, result.Value) : CreatedAtAction(nameof(Get), new { id = result.Value.FilingID }, result.Value);
    }

    [Authorize(Roles = "Admin"), HttpPut("{id:int}")]
    public async Task<ActionResult<ClaimDetailDto>> Update(int id, [FromBody] UpdateClaimDto request, CancellationToken cancellationToken) => Ok(await service.UpdateAsync(id, request, cancellationToken));

    [Authorize(Roles = "Admin"), HttpPatch("{id:int}/status")]
    public async Task<ActionResult<ClaimSummaryDto>> UpdateStatus(int id, [FromBody] UpdateClaimStatusDto request, CancellationToken cancellationToken) => Ok(await service.UpdateStatusAsync(id, request, cancellationToken));

    [Authorize(Roles = "Admin"), HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, [FromHeader(Name = "If-Match")] string? rowVersion, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, rowVersion ?? string.Empty, cancellationToken);
        return NoContent();
    }

    [Authorize(Roles = "Admin"), HttpGet("report")]
    public async Task<ActionResult<IReadOnlyList<RiskReportDto>>> Report([FromQuery] int? propertyId, CancellationToken cancellationToken)
    { var parameter = propertyId.HasValue ? (object)propertyId.Value : DBNull.Value; var report = await db.Database.SqlQueryRaw<RiskReportDto>("EXEC dbo.sp_GetFilingRiskReport @PropertyID = {0}", parameter).ToListAsync(cancellationToken); return Ok(report); }

    [Authorize(Roles = "Admin"), HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string? status, [FromQuery] string? filingType, [FromQuery] string? city, CancellationToken cancellationToken)
    {
        var result = await service.SearchAsync(status, filingType, city, 1, 100, cancellationToken);
        var csv = new StringBuilder("FilingID,Address,City,State,FilingType,DateFiled,Status,ClaimantName,RiskScore\n");
        foreach (var item in result.Items) csv.AppendLine(string.Join(',', item.FilingID, Csv(item.Address), Csv(item.City), Csv(item.State), Csv(item.FilingType), item.DateFiled.ToString("yyyy-MM-dd"), Csv(item.Status), Csv(item.ClaimantName), item.RiskScore?.ToString("0.000") ?? string.Empty));
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", "filings.csv");
    }

    private static string Csv(string? value)
    {
        var safe = value ?? string.Empty;
        if (safe.Length > 0 && safe[0] is '=' or '+' or '-' or '@') safe = "'" + safe;
        return $"\"{safe.Replace("\"", "\"\"")}\"";
    }
}
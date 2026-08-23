using Microsoft.AspNetCore.Mvc;
using System.Text;
using TitleClaimTracker.Core.DTOs;
using TitleClaimTracker.Infrastructure.Services;

namespace TitleClaimTracker.Controllers;

[ApiController, Route("api/filings")]
public sealed class FilingsController(IFilingService service) : ControllerBase
{
    [HttpGet("{id:int}")]
    public async Task<ActionResult<FilingSummaryDto>> Get(int id, CancellationToken cancellationToken) => Ok(await service.GetAsync(id, cancellationToken));

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string? status, [FromQuery] string? filingType, [FromQuery] string? city, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
    {
        var result = await service.SearchAsync(status, filingType, city, page, pageSize, cancellationToken);
        return Ok(new { items = result.Items, total = result.Total, page, pageSize });
    }

    [HttpPost]
    public async Task<ActionResult<FilingSummaryDto>> Create(CreateFilingDto request, CancellationToken cancellationToken) => CreatedAtAction(nameof(Get), new { id = (await service.CreateAsync(request, cancellationToken)).FilingID }, await service.CreateAsync(request, cancellationToken));

    [HttpPatch("{id:int}/status")]
    public async Task<ActionResult<FilingSummaryDto>> UpdateStatus(int id, UpdateFilingStatusDto request, CancellationToken cancellationToken) => Ok(await service.UpdateStatusAsync(id, request.Status, cancellationToken));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken) { await service.DeleteAsync(id, cancellationToken); return NoContent(); }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string? status, [FromQuery] string? filingType, [FromQuery] string? city, CancellationToken cancellationToken)
    {
        var result = await service.SearchAsync(status, filingType, city, 1, 100, cancellationToken);
        var csv = new StringBuilder("FilingID,Address,City,State,FilingType,DateFiled,Status,ClaimantName,RiskScore\n");
        foreach (var item in result.Items) csv.AppendLine(string.Join(',', item.FilingID, Csv(item.Address), Csv(item.City), item.State, Csv(item.FilingType), item.DateFiled.ToString("yyyy-MM-dd"), Csv(item.Status), Csv(item.ClaimantName), item.RiskScore?.ToString("0.000") ?? ""));
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", "filings.csv");
    }
    private static string Csv(string? value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
}

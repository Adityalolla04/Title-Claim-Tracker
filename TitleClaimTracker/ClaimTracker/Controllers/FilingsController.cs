// File: TitleClaimTracker/Controllers/FilingsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using TitleClaimTracker.Core.DTOs;
using TitleClaimTracker.Domain.Entities;
using TitleClaimTracker.Infrastructure.Data;
using TitleClaimTracker.Infrastructure.Services;
using TitleClaimTracker.Infrastructure.Security;

namespace TitleClaimTracker.Controllers;

[ApiController, Authorize, Route("api/filings")]
public sealed class FilingsController(IFilingService service, TitleClaimDbContext db, ICurrentUserAccessor currentUser, IClaimDocumentAnalysisService documentAnalysis) : ControllerBase
{
    private const int MaxDocumentBytes = 10 * 1024 * 1024;
    private const int MaxDocumentsPerClaim = 5;

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ClaimDetailDto>> Get(int id, CancellationToken cancellationToken) => Ok(await service.GetAsync(id, cancellationToken));

    [HttpGet("{id:int}/documents")]
    public async Task<ActionResult<IReadOnlyList<ClaimDocumentDto>>> Documents(int id, CancellationToken cancellationToken)
    {
        await service.GetAsync(id, cancellationToken);
        var documents = await db.ClaimDocuments.AsNoTracking()
            .Where(document => document.FilingID == id)
            .OrderBy(document => document.UploadedUtc)
            .Select(document => new ClaimDocumentDto(document.ClaimDocumentID, document.FilingID, document.FileName, document.FileSizeBytes, document.UploadedUtc))
            .ToListAsync(cancellationToken);
        return Ok(documents);
    }

    [HttpPost("{id:int}/documents"), Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxDocumentBytes + 64 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxDocumentBytes + 64 * 1024)]
    public async Task<ActionResult<ClaimDocumentDto>> UploadDocument(int id, IFormFile? file, CancellationToken cancellationToken)
    {
        await service.GetAsync(id, cancellationToken);
        if (file is null || file.Length == 0) return BadRequest(new { error = "Choose a PDF file to attach." });
        if (file.Length > MaxDocumentBytes) return BadRequest(new { error = "Each PDF must be 10 MB or smaller." });
        if (!string.Equals(Path.GetExtension(file.FileName), ".pdf", StringComparison.OrdinalIgnoreCase)) return BadRequest(new { error = "Only PDF documents are accepted." });

        var documentCount = await db.ClaimDocuments.CountAsync(document => document.FilingID == id, cancellationToken);
        if (documentCount >= MaxDocumentsPerClaim) return BadRequest(new { error = $"A claim can have at most {MaxDocumentsPerClaim} supporting PDFs." });

        await using var stream = new MemoryStream((int)file.Length);
        await file.CopyToAsync(stream, cancellationToken);
        var content = stream.ToArray();
        if (!HasPdfSignature(content)) return BadRequest(new { error = "The selected file does not have a valid PDF signature." });

        var safeName = Path.GetFileName(file.FileName);
        if (safeName.Length > 255) safeName = safeName[^255..];
        var document = new ClaimDocument
        {
            FilingID = id,
            UploadedByUserId = currentUser.RequireUserId(),
            FileName = safeName,
            ContentType = "application/pdf",
            FileSizeBytes = content.Length,
            Content = content,
            UploadedUtc = DateTime.UtcNow
        };
        db.ClaimDocuments.Add(document);
        await db.SaveChangesAsync(cancellationToken);
        var metadata = new ClaimDocumentDto(document.ClaimDocumentID, document.FilingID, document.FileName, document.FileSizeBytes, document.UploadedUtc);
        return CreatedAtAction(nameof(DownloadDocument), new { id, documentId = document.ClaimDocumentID }, metadata);
    }

    [HttpGet("{id:int}/documents/{documentId:long}")]
    public async Task<IActionResult> DownloadDocument(int id, long documentId, CancellationToken cancellationToken)
    {
        await service.GetAsync(id, cancellationToken);
        var document = await db.ClaimDocuments.AsNoTracking()
            .SingleOrDefaultAsync(item => item.FilingID == id && item.ClaimDocumentID == documentId, cancellationToken);
        return document is null ? NotFound() : File(document.Content, "application/pdf", document.FileName);
    }

    [Authorize(Roles = "Admin"), HttpPost("{id:int}/documents/{documentId:long}/analysis")]
    public async Task<ActionResult<ClaimDocumentAnalysisDto>> AnalyzeDocument(int id, long documentId, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await documentAnalysis.AnalyzeAsync(id, documentId, cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            return UnprocessableEntity(new { error = exception.Message });
        }
    }

    [Authorize(Roles = "Admin"), HttpGet("{id:int}/audit")]
    public async Task<ActionResult<IReadOnlyList<AuditLogEntryDto>>> Audit(int id, CancellationToken cancellationToken) => Ok(await service.GetAuditHistoryAsync(id, cancellationToken));

    [HttpGet]
    public async Task<ActionResult<PagedClaimsDto>> Search([FromQuery] string? status, [FromQuery] string? filingType, [FromQuery] string? city, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize is < 1 or > 100)
        {
            return BadRequest(new { error = "Page must be at least 1 and pageSize must be between 1 and 100." });
        }

        var result = await service.SearchAsync(status, filingType, city, search, page, pageSize, cancellationToken);
        return Ok(new PagedClaimsDto(result.Items, result.Total, page, pageSize));
    }

    [HttpGet("analytics")]
    public async Task<ActionResult<ClaimAnalyticsDto>> Analytics(CancellationToken cancellationToken) => Ok(await service.GetAnalyticsAsync(cancellationToken));

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

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ClaimDetailDto>> Update(int id, [FromBody] UpdateClaimDto request, CancellationToken cancellationToken) => Ok(await service.UpdateAsync(id, request, cancellationToken));

    [Authorize(Roles = "Admin"), HttpPatch("{id:int}/status")]
    public async Task<ActionResult<ClaimSummaryDto>> UpdateStatus(int id, [FromBody] UpdateClaimStatusDto request, CancellationToken cancellationToken) => Ok(await service.UpdateStatusAsync(id, request, cancellationToken));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, [FromHeader(Name = "If-Match")] string? rowVersion, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, rowVersion ?? string.Empty, cancellationToken);
        return NoContent();
    }

    [Authorize(Roles = "Admin"), HttpGet("report")]
    public async Task<ActionResult<IReadOnlyList<RiskReportDto>>> Report([FromQuery] int? propertyId, CancellationToken cancellationToken)
    { var parameter = propertyId.HasValue ? (object)propertyId.Value : DBNull.Value; var report = await db.Database.SqlQueryRaw<RiskReportDto>("EXEC dbo.sp_GetFilingRiskReport @PropertyID = {0}", parameter).ToListAsync(cancellationToken); return Ok(report); }

    [Authorize(Roles = "Admin"), HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string? status, [FromQuery] string? filingType, [FromQuery] string? city, [FromQuery] string? search, CancellationToken cancellationToken)
    {
        var items = await service.ExportAsync(status, filingType, city, search, cancellationToken);
        var csv = new StringBuilder("FilingID,Address,City,State,FilingType,DateFiled,Status,ClaimantName,RiskScore\n");
        foreach (var item in items) csv.AppendLine(string.Join(',', item.FilingID, Csv(item.Address), Csv(item.City), Csv(item.State), Csv(item.FilingType), item.DateFiled.ToString("yyyy-MM-dd"), Csv(item.Status), Csv(item.ClaimantName), item.RiskScore?.ToString("0.000") ?? string.Empty));
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", "filings.csv");
    }

    private static string Csv(string? value)
    {
        var safe = value ?? string.Empty;
        if (safe.Length > 0 && safe[0] is '=' or '+' or '-' or '@') safe = "'" + safe;
        return $"\"{safe.Replace("\"", "\"\"")}\"";
    }

    private static bool HasPdfSignature(byte[] content) => content.Length >= 5 && content.AsSpan(0, 5).SequenceEqual("%PDF-"u8);
}
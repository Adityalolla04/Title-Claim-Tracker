using System.Text;
using Microsoft.EntityFrameworkCore;
using TitleClaimTracker.Core.DTOs;
using TitleClaimTracker.Infrastructure.Data;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace TitleClaimTracker.Infrastructure.Services;

public interface IClaimDocumentAnalysisService
{
    Task<ClaimDocumentAnalysisDto> AnalyzeAsync(int filingId, long documentId, CancellationToken cancellationToken);
}

public sealed class ClaimDocumentAnalysisService(
    TitleClaimDbContext db,
    IFilingService filingService,
    IClaimExtractionService extractionService) : IClaimDocumentAnalysisService
{
    private const int MaxPages = 25;
    private const int MaxExtractedCharacters = 24000;

    public async Task<ClaimDocumentAnalysisDto> AnalyzeAsync(int filingId, long documentId, CancellationToken cancellationToken)
    {
        await filingService.GetAsync(filingId, cancellationToken);
        var document = await db.ClaimDocuments.AsNoTracking()
            .Where(item => item.FilingID == filingId && item.ClaimDocumentID == documentId)
            .Select(item => new { item.Content, item.FileName })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("The supporting document was not found.");

        var text = new StringBuilder(Math.Min(MaxExtractedCharacters, 4096));
        var analyzedPages = 0;
        int totalPages;
        try
        {
            using var pdf = PdfDocument.Open(document.Content);
            totalPages = pdf.NumberOfPages;
            foreach (var page in pdf.GetPages().Take(MaxPages))
            {
                cancellationToken.ThrowIfCancellationRequested();
                analyzedPages++;
                var pageText = ContentOrderTextExtractor.GetText(page);
                var remaining = MaxExtractedCharacters - text.Length;
                if (remaining <= 0) break;
                text.AppendLine(pageText.Length > remaining ? pageText[..remaining] : pageText);
            }
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
        {
            throw new InvalidOperationException("The attachment could not be parsed as a readable PDF.", exception);
        }

        if (string.IsNullOrWhiteSpace(text.ToString()))
        {
            throw new InvalidOperationException("No selectable text was found. This may be a scanned/image-only PDF; OCR is not enabled.");
        }

        var extraction = await extractionService.ExtractAsync(text.ToString(), cancellationToken);
        return new ClaimDocumentAnalysisDto(
            documentId, document.FileName, totalPages, analyzedPages, extraction.Mode,
            extraction.Summary, extraction.ClaimantName, extraction.Address, extraction.City,
            extraction.State, extraction.IssueDescription, extraction.RelevantDates, extraction.MissingFields);
    }
}

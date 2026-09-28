using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using TitleClaimTracker.Core.DTOs;
using TitleClaimTracker.Domain.Entities;
using TitleClaimTracker.Infrastructure.Data;
using TitleClaimTracker.Infrastructure.Services;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace TitleClaimTracker.Tests.UnitTests;

public sealed class ClaimDocumentAnalysisServiceTests
{
    [Fact]
    public async Task AnalyzeAsync_ExtractsSelectablePdfTextForClaimSummary()
    {
        await using var context = CreateContext();
        const string expectedText = "Supporting title record for Jane Doe";
        var documentId = await AddDocumentAsync(context, CreatePdf(expectedText));
        var extraction = new StructuredClaimExtraction("deterministic", "Jane Doe", "1 Main Street", "Austin", "TX", "Lien detail", [], [], [], "Document summary", null, null, null, null);
        var extractionService = new Mock<IClaimExtractionService>();
        extractionService.Setup(service => service.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(extraction);
        var service = CreateService(context, extractionService.Object);

        var result = await service.AnalyzeAsync(1, documentId, CancellationToken.None);

        Assert.Equal("Document summary", result.Summary);
        Assert.Equal("Jane Doe", result.ClaimantName);
        Assert.Equal(1, result.TotalPages);
        Assert.Equal(1, result.AnalyzedPages);
        extractionService.Verify(extractor => extractor.ExtractAsync(It.Is<string>(text => text.Contains(expectedText, StringComparison.Ordinal)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnalyzeAsync_ImageOnlyPdfReturnsActionableOcrMessage()
    {
        await using var context = CreateContext();
        var documentId = await AddDocumentAsync(context, CreatePdf(null));
        var service = CreateService(context, Mock.Of<IClaimExtractionService>());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.AnalyzeAsync(1, documentId, CancellationToken.None));

        Assert.Contains("scanned/image-only PDF", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ClaimDocumentAnalysisService CreateService(TitleClaimDbContext context, IClaimExtractionService extractionService)
    {
        var claimService = new Mock<IFilingService>();
        claimService.Setup(service => service.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(
            new ClaimDetailDto(1, 1, "1 Main Street", "Austin", "TX", null, "Lien", DateTime.UtcNow, "New", null, null, null, "Manual", null, null, "AA==", []));
        return new ClaimDocumentAnalysisService(context, claimService.Object, extractionService);
    }

    private static TitleClaimDbContext CreateContext() => new(new DbContextOptionsBuilder<TitleClaimDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

    private static async Task<long> AddDocumentAsync(TitleClaimDbContext context, byte[] content)
    {
        var document = new ClaimDocument
        {
            FilingID = 1,
            UploadedByUserId = "user-1",
            FileName = "evidence.pdf",
            FileSizeBytes = content.Length,
            Content = content,
            UploadedUtc = DateTime.UtcNow
        };
        context.ClaimDocuments.Add(document);
        await context.SaveChangesAsync();
        return document.ClaimDocumentID;
    }

    private static byte[] CreatePdf(string? text)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        if (!string.IsNullOrEmpty(text))
        {
            var font = builder.AddStandard14Font(Standard14Font.Helvetica);
            page.AddText(text, 12, new PdfPoint(40, 700), font);
        }

        return builder.Build();
    }
}

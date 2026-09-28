namespace TitleClaimTracker.Domain.Entities;

public sealed class ClaimDocument
{
    public long ClaimDocumentID { get; set; }
    public int FilingID { get; set; }
    public string UploadedByUserId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/pdf";
    public int FileSizeBytes { get; set; }
    public byte[] Content { get; set; } = [];
    public DateTime UploadedUtc { get; set; }

    public LegalFiling? Filing { get; set; }
}
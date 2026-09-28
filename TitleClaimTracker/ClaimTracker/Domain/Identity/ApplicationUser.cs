using Microsoft.AspNetCore.Identity;
using TitleClaimTracker.Domain.Entities;

namespace TitleClaimTracker.Domain.Identity;

public sealed class ApplicationUser : IdentityUser
{
    public string? DisplayName { get; set; }
    public DateTime CreatedUtc { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<LegalFiling> SubmittedFilings { get; set; } = new List<LegalFiling>();
    public ICollection<LegalFiling> DeletedFilings { get; set; } = new List<LegalFiling>();
    public ICollection<ClaimStatusHistory> StatusHistoryEntries { get; set; } = new List<ClaimStatusHistory>();
    public ICollection<TriageAttempt> TriageAttempts { get; set; } = new List<TriageAttempt>();
    public ICollection<TriageFeedback> TriageFeedbackEntries { get; set; } = new List<TriageFeedback>();
    public ICollection<AuditLog> AuditEntries { get; set; } = new List<AuditLog>();
    public ICollection<IdempotencyRecord> IdempotencyRecords { get; set; } = new List<IdempotencyRecord>();
}
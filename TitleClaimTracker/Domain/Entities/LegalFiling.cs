using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TitleClaimTracker.Domain.Identity;

namespace TitleClaimTracker.Domain.Entities;

[Table("LegalFilings")]
public sealed class LegalFiling
{
    public static readonly string[] ValidStatuses = ["New", "Under Review", "Escalated", "Resolved", "Closed", "Pending Review", "Active Investigation"];

    [Key]
    public int FilingID { get; set; }
    public int PropertyID { get; set; }
    public int FilingTypeID { get; set; }
    public DateTime DateFiled { get; set; }

    [Required, MaxLength(50)]
    public string Status { get; set; } = "New";

    [MaxLength(255)]
    public string? ClaimantName { get; set; }
    public string? Notes { get; set; }
    public double? RiskScore { get; set; }
    public string? SubmittedByUserId { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public string CreationSource { get; set; } = "Manual";
    public string? IssueTags { get; set; }
    public decimal? ClassificationScore { get; set; }
    public long? ModelVersionID { get; set; }
    public byte TriagePriority { get; set; } = 3;
    public string TriageRuleVersion { get; set; } = "triage-v1";
    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    public bool IsDeleted { get; set; }
    public DateTime? DeletedUtc { get; set; }
    public string? DeletedByUserId { get; set; }
    public bool IsSyntheticDemoData { get; set; }

    public Property? Property { get; set; }
    public FilingType? FilingType { get; set; }
    public ApplicationUser? SubmittedByUser { get; set; }
    public ApplicationUser? DeletedByUser { get; set; }
    public ModelVersion? ModelVersion { get; set; }
    public ICollection<ClaimStatusHistory> StatusHistory { get; set; } = new List<ClaimStatusHistory>();
    public ICollection<TriageAttempt> TriageAttempts { get; set; } = new List<TriageAttempt>();
}

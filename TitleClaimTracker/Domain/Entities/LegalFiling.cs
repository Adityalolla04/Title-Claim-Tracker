using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TitleClaimTracker.Domain.Entities;

[Table("LegalFilings")]
public sealed class LegalFiling
{
    public static readonly string[] ValidStatuses = ["New", "Under Review", "Escalated", "Resolved", "Closed"];

    [Key]
    public int FilingID { get; set; }
    public int PropertyID { get; set; }
    public int FilingTypeID { get; set; }
    public DateTime DateFiled { get; set; }

    [Required, MaxLength(50)]
    public string Status { get; set; } = "New";

    [MaxLength(200)]
    public string? ClaimantName { get; set; }
    public string? Notes { get; set; }
    public double? RiskScore { get; set; }
    public Property? Property { get; set; }
    public FilingType? FilingType { get; set; }
}

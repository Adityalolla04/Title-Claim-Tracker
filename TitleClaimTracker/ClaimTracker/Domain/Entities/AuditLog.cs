using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TitleClaimTracker.Domain.Identity;

namespace TitleClaimTracker.Domain.Entities;

[Table("AuditLog")]
public sealed class AuditLog
{
    [Key]
    public long AuditID { get; set; }
    [Required, MaxLength(128)]
    public string TableName { get; set; } = string.Empty;
    public int RecordID { get; set; }
    [MaxLength(100)]
    public string? ColumnChanged { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime? ChangeDate { get; set; }
    public string? ActorUserId { get; set; }
    public string Action { get; set; } = "Update";

    public ApplicationUser? ActorUser { get; set; }
}

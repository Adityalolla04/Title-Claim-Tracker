using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TitleClaimTracker.Domain.Entities;

[Table("Properties")]
public sealed class Property
{
    [Key]
    public int PropertyID { get; set; }

    [Required, MaxLength(255)]
    public string Address { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string City { get; set; } = string.Empty;

    [Required, StringLength(2)]
    public string State { get; set; } = string.Empty;

    public string? LegalDescription { get; set; }
    public DateTime CreatedDate { get; set; }
    public ICollection<LegalFiling> LegalFilings { get; set; } = new List<LegalFiling>();
}

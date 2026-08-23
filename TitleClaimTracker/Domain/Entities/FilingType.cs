using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TitleClaimTracker.Domain.Entities;

[Table("FilingTypes")]
public sealed class FilingType
{
    [Key]
    public int FilingTypeID { get; set; }

    [Required, MaxLength(50)]
    public string TypeName { get; set; } = string.Empty;
    public ICollection<LegalFiling> LegalFilings { get; set; } = new List<LegalFiling>();
}

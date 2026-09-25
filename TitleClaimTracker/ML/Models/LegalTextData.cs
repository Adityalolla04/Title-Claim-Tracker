// File: TitleClaimTracker/ML/Models/LegalTextData.cs
using Microsoft.ML.Data;

namespace TitleClaimTracker.ML.Models;

public sealed class LegalTextData
{
    [LoadColumn(0)]
    public string UnstructuredText { get; set; } = string.Empty;

    [LoadColumn(1)]
    public string FilingType { get; set; } = string.Empty;

    [LoadColumn(2)]
    public float RiskScore { get; set; }
}

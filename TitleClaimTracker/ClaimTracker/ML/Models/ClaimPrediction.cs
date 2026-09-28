// File: TitleClaimTracker/ML/Models/ClaimPrediction.cs
using Microsoft.ML.Data;

namespace TitleClaimTracker.ML.Models;

public sealed class ClaimPrediction
{
    [ColumnName("PredictedLabel")]
    public string PredictedFilingType { get; set; } = string.Empty;

    public float[] Score { get; set; } = Array.Empty<float>();

    public float Probability { get; set; }

    public float CalculatedRisk { get; set; }
}

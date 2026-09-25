// File: TitleClaimTracker/ML/Services/TitleClaimMlEngine.cs
using Microsoft.Extensions.ML;
using Microsoft.ML;
using TitleClaimTracker.ML.Models;

namespace TitleClaimTracker.ML.Services;

public sealed class TitleClaimMlEngine(
    PredictionEnginePool<LegalTextData, ClaimPrediction> predictionPool,
    IWebHostEnvironment environment,
    ILogger<TitleClaimMlEngine> logger) : ITitleClaimMlEngine
{
    private static readonly LegalTextData[] SeedData =
    [
        new() { UnstructuredText = "claim against the title ownership and defective chain of title", FilingType = "Title Claim", RiskScore = .82f },
        new() { UnstructuredText = "owner disputes recorded title and requests quiet title relief", FilingType = "Title Claim", RiskScore = .88f },
        new() { UnstructuredText = "unpaid mortgage lien and notice of foreclosure", FilingType = "Lien", RiskScore = .91f },
        new() { UnstructuredText = "mechanics lien recorded for unpaid construction invoice", FilingType = "Lien", RiskScore = .84f },
        new() { UnstructuredText = "utility company requests access across the parcel", FilingType = "Easement", RiskScore = .48f },
        new() { UnstructuredText = "right of way and access easement is being contested", FilingType = "Easement", RiskScore = .62f },
        new() { UnstructuredText = "buyer alleges forged deed and challenges conveyance", FilingType = "Deed Dispute", RiskScore = .86f },
        new() { UnstructuredText = "deed signature is invalid and transfer is disputed", FilingType = "Deed Dispute", RiskScore = .79f }
    ];

    public ClaimPrediction Predict(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var input = new LegalTextData { UnstructuredText = text.Trim() };
        var prediction = predictionPool.Predict(input);
        var scores = prediction.Score ?? Array.Empty<float>();
        var probability = scores.Length == 0 ? 0f : SoftmaxMaximum(scores);
        var predictedType = string.IsNullOrWhiteSpace(prediction.PredictedFilingType) ? "Title Claim" : prediction.PredictedFilingType;
        var calculatedRisk = Math.Clamp(RiskHeuristic(input.UnstructuredText, predictedType), 0f, 1f);
        prediction.PredictedFilingType = predictedType;
        prediction.Probability = probability;
        prediction.CalculatedRisk = calculatedRisk;
        return prediction;
    }

    public async Task EnsureModelAsync(CancellationToken cancellationToken = default)
    {
        var modelPath = Path.Combine(environment.ContentRootPath, "ML", "TitleClaimModel.zip");
        BootstrapModel(modelPath, logger);
        await Task.CompletedTask;
        cancellationToken.ThrowIfCancellationRequested();
    }

    public static void BootstrapModel(string modelPath, ILogger? logger = null)
    {
        if (File.Exists(modelPath)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
        var mlContext = new MLContext(seed: 42);
        var data = mlContext.Data.LoadFromEnumerable(SeedData);
        var pipeline = mlContext.Transforms.Conversion.MapValueToKey("Label", nameof(LegalTextData.FilingType))
            .Append(mlContext.Transforms.Text.FeaturizeText("Features", nameof(LegalTextData.UnstructuredText)))
            .Append(mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy("Label", "Features"))
            .Append(mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel"));
        var model = pipeline.Fit(data);
        using var stream = File.Create(modelPath);
        mlContext.Model.Save(model, data.Schema, stream);
        logger?.LogInformation("Bootstrapped native ML.NET model at {ModelPath}", modelPath);
    }

    private static float SoftmaxMaximum(float[] scores)
    {
        var max = scores.Max();
        var total = scores.Sum(score => MathF.Exp(score - max));
        return total <= 0 ? 0 : MathF.Exp(max - max) / total;
    }

    private static float RiskHeuristic(string text, string filingType)
    {
        var urgentTerms = new[] { "foreclosure", "forged", "fraud", "unpaid", "invalid", "dispute" };
        var termBoost = urgentTerms.Count(term => text.Contains(term, StringComparison.OrdinalIgnoreCase)) * .08f;
        var typeBase = filingType switch { "Lien" => .72f, "Title Claim" => .68f, "Deed Dispute" => .75f, "Easement" => .42f, _ => .5f };
        return Math.Clamp(typeBase + termBoost, 0f, 1f);
    }
}

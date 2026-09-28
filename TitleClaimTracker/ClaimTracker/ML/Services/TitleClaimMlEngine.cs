// File: TitleClaimTracker/ML/Services/TitleClaimMlEngine.cs
using Microsoft.Extensions.ML;
using Microsoft.ML;
using TitleClaimTracker.ML.Models;
using TitleClaimTracker.ML.Training;

namespace TitleClaimTracker.ML.Services;

public sealed class TitleClaimMlEngine(
    PredictionEnginePool<LegalTextData, ClaimPrediction> predictionPool,
    IWebHostEnvironment environment,
    ILogger<TitleClaimMlEngine> logger) : ITitleClaimMlEngine
{
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
        var trainingDirectory = Path.Combine(Path.GetDirectoryName(modelPath)!, "Training");
        var dataPath = Path.Combine(trainingDirectory, "training.csv");
        if (!File.Exists(dataPath))
        {
            logger?.LogWarning("ML model and reproducible dataset are missing; assisted intake is unavailable until training runs.");
            return;
        }

        ModelTrainingRunner.Run(Path.GetDirectoryName(modelPath)!, message => logger?.LogInformation("{TrainingMessage}", message));
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

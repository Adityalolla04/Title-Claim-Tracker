// File: TitleClaimTracker/ML/Services/ITitleClaimMlEngine.cs
using TitleClaimTracker.ML.Models;

namespace TitleClaimTracker.ML.Services;

public interface ITitleClaimMlEngine
{
    ClaimPrediction Predict(string text);
    Task EnsureModelAsync(CancellationToken cancellationToken = default);
}

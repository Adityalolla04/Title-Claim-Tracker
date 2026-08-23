namespace TitleClaimTracker.Core.Exceptions;

public sealed class InvalidFilingStatusTransitionException(string current, string requested)
    : InvalidOperationException($"A filing cannot transition from '{current}' to '{requested}'.")
{
    public string CurrentStatus { get; } = current;
    public string RequestedStatus { get; } = requested;
}

public sealed class AiExtractionConfidenceException(double confidence, double minimum)
    : InvalidOperationException($"AI extraction confidence {confidence:P0} is below the required threshold {minimum:P0}.")
{
    public double Confidence { get; } = confidence;
    public double MinimumConfidence { get; } = minimum;
}

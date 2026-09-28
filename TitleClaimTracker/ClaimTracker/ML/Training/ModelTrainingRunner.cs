using System.Globalization;
using System.Text.Json;
using Microsoft.ML;
using TitleClaimTracker.ML.Models;

namespace TitleClaimTracker.ML.Training;

public static class ModelTrainingRunner
{
    private const int Seed = 42;

    public static void Run(string mlDirectory, Action<string> writeLine)
    {
        var trainingDirectory = Path.Combine(mlDirectory, "Training");
        var dataPath = Path.Combine(trainingDirectory, "training.csv");
        var modelPath = Path.Combine(mlDirectory, "TitleClaimModel.zip");
        var evaluationPath = Path.Combine(trainingDirectory, "evaluation.json");
        var rows = ReadRows(dataPath);
        var mlContext = new MLContext(Seed);
        var groups = rows.Select(row => row.SourceGroup).Distinct(StringComparer.Ordinal).OrderBy(group => group, StringComparer.Ordinal).ToArray();
        var testGroups = groups.Where((_, index) => index % 4 == 0).ToHashSet(StringComparer.Ordinal);
        var trainingRows = rows.Where(row => !testGroups.Contains(row.SourceGroup)).Select(row => row.Data).ToArray();
        var testRows = rows.Where(row => testGroups.Contains(row.SourceGroup)).Select(row => row.Data).ToArray();
        var trainingData = mlContext.Data.LoadFromEnumerable(trainingRows);
        var testData = mlContext.Data.LoadFromEnumerable(testRows);
        var pipeline = CreatePipeline(mlContext);
        var model = pipeline.Fit(trainingData);
        var predictions = model.Transform(testData);
        var metrics = mlContext.MulticlassClassification.Evaluate(predictions, labelColumnName: "Label", predictedLabelColumnName: "PredictedLabel");

        Directory.CreateDirectory(mlDirectory);
        using (var stream = File.Create(modelPath))
        {
            mlContext.Model.Save(model, trainingData.Schema, stream);
        }

        var report = new
        {
            modelName = "title-claim-sdca",
            modelVersion = $"csv-{DateTime.UtcNow:yyyyMMddHHmmss}",
            datasetVersion = "synthetic-v1",
            datasetProvenance = "Synthetic, repository-controlled examples; source-group holdout; seed 42.",
            seed = Seed,
            trainingSamples = trainingRows.Length,
            evaluationSamples = testRows.Length,
            testGroups,
            accuracy = metrics.MicroAccuracy,
            macroF1 = metrics.MacroAccuracy,
            weightedF1 = metrics.ConfusionMatrix is null ? 0 : metrics.ConfusionMatrix.PerClassPrecision.Select((precision, index) => F1(precision, metrics.ConfusionMatrix.PerClassRecall[index]) * metrics.ConfusionMatrix.Counts[index].Sum()).Sum() / metrics.ConfusionMatrix.Counts.Sum(row => row.Sum()),
            perClass = metrics.ConfusionMatrix is null ? [] : metrics.ConfusionMatrix.PerClassPrecision.Select((precision, index) => new
            {
                classIndex = index,
                precision,
                recall = metrics.ConfusionMatrix.PerClassRecall[index],
                f1 = F1(precision, metrics.ConfusionMatrix.PerClassRecall[index])
            }).ToArray(),
            confusionMatrix = metrics.ConfusionMatrix?.Counts,
            limitations = new[] { "The dataset is synthetic and small.", "The score is an uncalibrated model score, not a probability of correctness.", "Evaluation groups are held out by source group to reduce template leakage." }
        };
        File.WriteAllText(evaluationPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        writeLine($"Trained {report.modelName} on {report.trainingSamples} rows; evaluated {report.evaluationSamples} rows.");
        writeLine($"Model: {modelPath}");
        writeLine($"Evaluation: {evaluationPath}");
    }

    public static IEstimator<ITransformer> CreatePipeline(MLContext mlContext) =>
        mlContext.Transforms.Conversion.MapValueToKey("Label", nameof(LegalTextData.FilingType))
            .Append(mlContext.Transforms.Text.FeaturizeText("Features", nameof(LegalTextData.UnstructuredText)))
            .Append(mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy("Label", "Features"))
            .Append(mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel"));

    private static IReadOnlyList<TrainingRow> ReadRows(string dataPath)
    {
        if (!File.Exists(dataPath)) throw new FileNotFoundException("The reproducible ML dataset was not found.", dataPath);
        return File.ReadLines(dataPath).Skip(1).Select((line, index) =>
        {
            var fields = ParseCsv(line);
            if (fields.Count != 4 || string.IsNullOrWhiteSpace(fields[0]) || string.IsNullOrWhiteSpace(fields[1]) || !float.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var risk) || risk is < 0 or > 1 || string.IsNullOrWhiteSpace(fields[3]))
            {
                throw new InvalidDataException($"Invalid training row {index + 2} in {dataPath}.");
            }
            return new TrainingRow(new LegalTextData { UnstructuredText = fields[0], FilingType = fields[1], RiskScore = risk }, fields[3]);
        }).GroupBy(row => row.Data.UnstructuredText.Trim(), StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToArray();
    }

    private static List<string> ParseCsv(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var character in line)
        {
            if (character == '"') { quoted = !quoted; continue; }
            if (character == ',' && !quoted) { fields.Add(current.ToString()); current.Clear(); continue; }
            current.Append(character);
        }
        fields.Add(current.ToString());
        return fields;
    }

    private static double F1(double precision, double recall) => precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall);

    private sealed record TrainingRow(LegalTextData Data, string SourceGroup);
}

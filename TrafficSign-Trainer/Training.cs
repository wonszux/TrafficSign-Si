using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using TrafficSign.Core;

namespace TrafficSign.Trainer;

public sealed record TrainingOptions(int Epochs = 30, double LearningRate = 0.01, int HiddenSize = 64, int Seed = 42);
public sealed record Metrics(int Count, double Loss, double Accuracy, double[] RecallPerClass, int[][] ConfusionMatrix);

public static class Training
{
    public static Metrics Evaluate(NeuralNetwork network, DatasetSample[] samples, int classCount)
    {
        if (samples.Length == 0) throw new InvalidDataException("Cannot evaluate an empty split.");
        var confusion = Enumerable.Range(0, classCount).Select(_ => new int[classCount]).ToArray();
        double loss = 0;
        int correct = 0;
        foreach (var sample in samples)
        {
            var prediction = network.Predict(sample.Inputs);
            int predicted = Array.IndexOf(prediction, prediction.Max());
            confusion[sample.LabelIndex][predicted]++;
            if (predicted == sample.LabelIndex) correct++;
            loss += network.Loss(sample.Inputs, sample.LabelIndex);
        }
        var recall = confusion.Select((row, k) => row.Sum() == 0 ? 0 : (double)row[k] / row.Sum()).ToArray();
        double averageLoss = loss / samples.Length;
        if (!double.IsFinite(averageLoss)) throw new ArithmeticException("Evaluation produced a non-finite loss.");
        return new Metrics(samples.Length, averageLoss, (double)correct / samples.Length, recall, confusion);
    }

    public static void Run(string directory, string modelPath, string? manifestPath, TrainingOptions options, TextWriter output)
    {
        ValidateOptions(options);
        string modelTarget = Path.GetFullPath(modelPath);
        string stem = Path.Combine(Path.GetDirectoryName(modelTarget)!, Path.GetFileNameWithoutExtension(modelTarget));
        string reportTarget = stem + ".report.json", splitTarget = stem + ".split.json";
        string[] targets = [modelTarget, reportTarget, splitTarget];
        if (targets.Distinct(StringComparer.OrdinalIgnoreCase).Count() != targets.Length || targets.Any(path => File.Exists(path) || Directory.Exists(path)))
            throw new IOException("An output path already exists or output paths collide. Choose a new model name.");
        var dataset = Dataset.Load(directory, manifestPath, options.Seed);
        output.WriteLine($"Classes: {dataset.Labels.Length}; images: {dataset.Samples.Length}; ignored files: {dataset.IgnoredFiles}");
        if (!dataset.CompleteGroupingMetadata)
            output.WriteLine("Warning: grouping metadata is incomplete; only identical-file leakage is checked.");
        var train = dataset.Samples.Where(sample => sample.Split == "train").ToArray();
        var validation = dataset.Samples.Where(sample => sample.Split == "validation").ToArray();
        var network = NeuralNetwork.Create(options.HiddenSize, dataset.Labels, options.Seed);
        var random = new Random(options.Seed);
        double bestLoss = double.PositiveInfinity;
        int bestEpoch = 0;
        ModelData? bestModel = null;
        var history = new List<object>();
        var stopwatch = Stopwatch.StartNew();
        for (int epoch = 1; epoch <= options.Epochs; epoch++)
        {
            random.Shuffle(train);
            foreach (var sample in train) network.TrainSample(sample.Inputs, sample.LabelIndex, options.LearningRate);
            var trainingMetrics = Evaluate(network, train, dataset.Labels.Length);
            var validationMetrics = Evaluate(network, validation, dataset.Labels.Length);
            output.WriteLine(FormattableString.Invariant($"Epoch {epoch}/{options.Epochs}: train loss={trainingMetrics.Loss:F6} accuracy={trainingMetrics.Accuracy:P2}; validation loss={validationMetrics.Loss:F6} accuracy={validationMetrics.Accuracy:P2}"));
            history.Add(new { Epoch = epoch, Train = trainingMetrics, Validation = validationMetrics });
            if (validationMetrics.Loss < bestLoss)
            {
                bestLoss = validationMetrics.Loss;
                bestEpoch = epoch;
                bestModel = network.GetModel();
            }
        }
        network = new NeuralNetwork(bestModel!);
        var selectedTrain = Evaluate(network, train, dataset.Labels.Length);
        var selectedValidation = Evaluate(network, validation, dataset.Labels.Length);
        int majorityClass = train.GroupBy(sample => sample.LabelIndex).OrderByDescending(group => group.Count()).ThenBy(group => group.Key).First().Key;
        double baselineValidation = (double)validation.Count(sample => sample.LabelIndex == majorityClass) / validation.Length;
        Directory.CreateDirectory(Path.GetDirectoryName(modelTarget)!);
        string temporaryStem = modelTarget + "." + Guid.NewGuid().ToString("N");
        string modelTemporary = temporaryStem + ".model.tmp";
        string reportTemporary = temporaryStem + ".report.tmp";
        string splitTemporary = temporaryStem + ".split.tmp";
        var published = new List<string>();
        try
        {
            ModelFile.Save(modelTemporary, bestModel!);
            string modelHash = HashFile(modelTemporary);
            var report = new
            {
                FormatVersion = 1, ModelSha256 = modelHash, DatasetFingerprint = dataset.Fingerprint,
                Labels = dataset.Labels, Options = options, BestEpoch = bestEpoch,
                CompleteGroupingMetadata = dataset.CompleteGroupingMetadata,
                GroupingLimitation = dataset.CompleteGroupingMetadata ? null : "Only identical files are grouped; related views may be unknown.",
                ClassCounts = dataset.Labels.Select((label, index) => new
                {
                    Label = label, Train = train.Count(sample => sample.LabelIndex == index),
                    Validation = validation.Count(sample => sample.LabelIndex == index),
                    Test = dataset.Samples.Count(sample => sample.LabelIndex == index && sample.Split == "test")
                }),
                Baseline = new { Label = dataset.Labels[majorityClass], ValidationAccuracy = baselineValidation },
                SelectedTrain = selectedTrain, SelectedValidation = selectedValidation, TestEvaluated = false,
                ElapsedSeconds = stopwatch.Elapsed.TotalSeconds, Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                History = history
            };
            WriteJsonNew(reportTemporary, report);
            WriteJsonNew(splitTemporary, dataset.ToManifest());
            Publish(splitTemporary, splitTarget);
            Publish(reportTemporary, reportTarget);
            Publish(modelTemporary, modelTarget); // The model is the completion marker and is always published last.
        }
        catch
        {
            foreach (string path in published) File.Delete(path);
            throw;
        }
        finally
        {
            foreach (string path in new[] { modelTemporary, reportTemporary, splitTemporary })
                if (File.Exists(path)) File.Delete(path);
        }
        output.WriteLine($"Saved best epoch {bestEpoch}: {modelTarget}");
        output.WriteLine("Test remains held out. Use evaluate with the saved model and split when tuning is finished.");
        void Publish(string temporary, string target)
        {
            File.Move(temporary, target, overwrite: false);
            published.Add(target);
        }
    }

    public static void EvaluateSaved(string modelPath, string directory, string splitPath, TextWriter output)
    {
        string reportPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(modelPath))!, Path.GetFileNameWithoutExtension(modelPath) + ".test-report.json");
        if (File.Exists(reportPath) || Directory.Exists(reportPath)) throw new IOException("Test report already exists. Preserve it or choose another model name.");
        ModelData model = ModelFile.Load(modelPath);
        var dataset = Dataset.Load(directory, splitPath, 0, requireCompleteSplit: true);
        if (!model.Labels.SequenceEqual(dataset.Labels, StringComparer.Ordinal))
            throw new InvalidDataException("Saved model labels do not match the dataset classes.");
        string modelHash = HashFile(modelPath);
        string trainingReportPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(modelPath))!, Path.GetFileNameWithoutExtension(modelPath) + ".report.json");
        using (var reportStream = File.OpenRead(trainingReportPath))
        using (var report = JsonDocument.Parse(reportStream))
        {
            var root = report.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("formatVersion", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int formatVersion) || formatVersion != 1 ||
                !root.TryGetProperty("modelSha256", out var hash) || hash.ValueKind != JsonValueKind.String || hash.GetString() != modelHash ||
                !root.TryGetProperty("datasetFingerprint", out var fingerprint) || fingerprint.ValueKind != JsonValueKind.String || fingerprint.GetString() != dataset.Fingerprint)
                throw new InvalidDataException("Model or split does not match the original training report.");
        }
        var test = dataset.Samples.Where(sample => sample.Split == "test").ToArray();
        var metrics = Evaluate(new NeuralNetwork(model), test, model.Labels.Length);
        var train = dataset.Samples.Where(sample => sample.Split == "train").ToArray();
        int majority = train.GroupBy(sample => sample.LabelIndex).OrderByDescending(group => group.Count()).ThenBy(group => group.Key).First().Key;
        string temporary = reportPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            WriteJsonNew(temporary, new
            {
                FormatVersion = 1, ModelSha256 = modelHash, DatasetFingerprint = dataset.Fingerprint,
                Labels = model.Labels, TestEvaluated = true, Test = metrics,
                CompleteGroupingMetadata = dataset.CompleteGroupingMetadata,
                Baseline = new { Label = model.Labels[majority], TestAccuracy = (double)test.Count(sample => sample.LabelIndex == majority) / test.Length }
            });
            File.Move(temporary, reportPath, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        output.WriteLine(FormattableString.Invariant($"Test accuracy={metrics.Accuracy:P2}; loss={metrics.Loss:F6}; n={metrics.Count}"));
        output.WriteLine($"Saved: {reportPath}");
    }

    public static void ValidateOptions(TrainingOptions options)
    {
        if (options.Epochs is < 1 or > 10_000) throw new ArgumentException("epochs must be between 1 and 10000.");
        if (options.HiddenSize is < 1 or > 2048) throw new ArgumentException("hidden-size must be between 1 and 2048.");
        if (!double.IsFinite(options.LearningRate) || options.LearningRate is <= 0 or > 10)
            throw new ArgumentException("learning-rate must be finite, greater than 0 and at most 10.");
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void WriteJsonNew<T>(string path, T value)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        JsonSerializer.Serialize(stream, value, ModelFile.JsonOptions);
    }
}

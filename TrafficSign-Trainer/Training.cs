using System.Diagnostics;
using System.Runtime.InteropServices;
using TrafficSign.Core;

namespace TrafficSign.Trainer;

public static class Training
{
    private sealed record TrainingResult(ModelData Model, int BestEpoch, List<EpochMetrics> History);

    public static void Run(string directory, string modelPath, string? manifestPath, TrainingOptions options, TextWriter output)
    {
        ValidateOptions(options);
        var paths = ExperimentPaths.ForModel(modelPath);
        paths.EnsureTrainingOutputsAvailable();
        var dataset = Dataset.Load(directory, manifestPath, options.Seed);
        DescribeDataset(dataset, output);

        var train = SelectSplit(dataset, DatasetSplit.Train);
        var validation = SelectSplit(dataset, DatasetSplit.Validation);
        var stopwatch = Stopwatch.StartNew();
        var result = Train(dataset.Labels, train, validation, options, output);
        var report = BuildTrainingReport(dataset, train, validation, options, result, stopwatch);
        ExperimentFiles.PublishTraining(paths, result.Model, dataset.ToManifest(), report, output);

        output.WriteLine($"Saved best epoch {result.BestEpoch}: {paths.Model}");
        output.WriteLine("Test remains held out. Use evaluate with the saved model and split when tuning is finished.");
    }

    public static void EvaluateSaved(string modelPath, string directory, string splitPath, TextWriter output)
    {
        var paths = ExperimentPaths.ForModel(modelPath);
        ExperimentFiles.EnsureAvailable(paths.TestReport);
        var model = ModelFile.Load(paths.Model);
        var dataset = Dataset.Load(directory, splitPath, 0, requireCompleteSplit: true);
        if (!model.Labels.SequenceEqual(dataset.Labels, StringComparer.Ordinal))
        {
            throw new InvalidDataException("Saved model labels do not match the dataset classes.");
        }
        string modelHash = ExperimentFiles.HashFile(paths.Model);
        ExperimentFiles.VerifyIdentity(paths, dataset, modelHash);

        var test = SelectSplit(dataset, DatasetSplit.Test);
        var metrics = Evaluate(new NeuralNetwork(model), test, model.Labels.Length);
        int majority = FindMajorityClass(SelectSplit(dataset, DatasetSplit.Train));
        var report = new TestReport
        {
            ModelSha256 = modelHash,
            DatasetFingerprint = dataset.Fingerprint,
            Labels = model.Labels,
            Test = metrics,
            CompleteGroupingMetadata = dataset.CompleteGroupingMetadata,
            Baseline = new TestBaseline(model.Labels[majority], MajorityAccuracy(test, majority))
        };
        ExperimentFiles.PublishTestReport(paths.TestReport, report, output);
        output.WriteLine(FormattableString.Invariant($"Test accuracy={metrics.Accuracy:P2}; loss={metrics.Loss:F6}; n={metrics.Count}"));
        output.WriteLine($"Saved: {paths.TestReport}");
    }

    public static Metrics Evaluate(NeuralNetwork network, DatasetSample[] samples, int classCount)
    {
        if (samples.Length == 0)
        {
            throw new InvalidDataException("Cannot evaluate an empty split.");
        }
        var confusion = Enumerable.Range(0, classCount).Select(_ => new int[classCount]).ToArray();
        double loss = 0;
        int correct = 0;
        foreach (var sample in samples)
        {
            var evaluation = network.EvaluateSample(sample.Inputs, sample.LabelIndex);
            confusion[sample.LabelIndex][evaluation.PredictedIndex]++;
            if (evaluation.PredictedIndex == sample.LabelIndex)
            {
                correct++;
            }
            loss += evaluation.Loss;
        }
        var recall = confusion.Select((row, index) => row.Sum() == 0 ? 0 : (double)row[index] / row.Sum()).ToArray();
        double averageLoss = loss / samples.Length;
        if (!double.IsFinite(averageLoss))
        {
            throw new ArithmeticException("Evaluation produced a non-finite loss.");
        }
        return new Metrics(samples.Length, averageLoss, (double)correct / samples.Length, recall, confusion);
    }

    private static TrainingResult Train(
        string[] labels, DatasetSample[] train, DatasetSample[] validation, TrainingOptions options, TextWriter output)
    {
        var network = NeuralNetwork.Create(options.HiddenSize, labels, options.Seed);
        var random = new Random(options.Seed);
        double bestLoss = double.PositiveInfinity;
        int bestEpoch = 0;
        ModelData? bestModel = null;
        var history = new List<EpochMetrics>();
        for (int epoch = 1; epoch <= options.Epochs; epoch++)
        {
            random.Shuffle(train);
            foreach (var sample in train)
            {
                network.TrainSample(sample.Inputs, sample.LabelIndex, options.LearningRate);
            }
            var trainingMetrics = Evaluate(network, train, labels.Length);
            var validationMetrics = Evaluate(network, validation, labels.Length);
            output.WriteLine(FormattableString.Invariant($"Epoch {epoch}/{options.Epochs}: train loss={trainingMetrics.Loss:F6} accuracy={trainingMetrics.Accuracy:P2}; validation loss={validationMetrics.Loss:F6} accuracy={validationMetrics.Accuracy:P2}"));
            history.Add(new EpochMetrics(epoch, trainingMetrics, validationMetrics));
            if (validationMetrics.Loss < bestLoss)
            {
                bestLoss = validationMetrics.Loss;
                bestEpoch = epoch;
                bestModel = network.GetModel();
            }
        }
        return new TrainingResult(bestModel!, bestEpoch, history);
    }

    private static TrainingReport BuildTrainingReport(
        Dataset dataset, DatasetSample[] train, DatasetSample[] validation,
        TrainingOptions options, TrainingResult result, Stopwatch stopwatch)
    {
        var selectedNetwork = new NeuralNetwork(result.Model);
        var selectedTrain = Evaluate(selectedNetwork, train, dataset.Labels.Length);
        var selectedValidation = Evaluate(selectedNetwork, validation, dataset.Labels.Length);
        int majority = FindMajorityClass(train);
        return new TrainingReport
        {
            DatasetFingerprint = dataset.Fingerprint,
            Labels = dataset.Labels,
            Options = options,
            BestEpoch = result.BestEpoch,
            CompleteGroupingMetadata = dataset.CompleteGroupingMetadata,
            GroupingLimitation = dataset.CompleteGroupingMetadata
                ? null : "Only identical files are grouped; related views may be unknown.",
            ClassCounts = dataset.Labels.Select((label, index) => new ClassCounts(
                label,
                train.Count(sample => sample.LabelIndex == index),
                validation.Count(sample => sample.LabelIndex == index),
                dataset.Samples.Count(sample => sample.LabelIndex == index && sample.Split == DatasetSplit.Test))).ToArray(),
            Baseline = new ValidationBaseline(dataset.Labels[majority], MajorityAccuracy(validation, majority)),
            SelectedTrain = selectedTrain,
            SelectedValidation = selectedValidation,
            ElapsedSeconds = stopwatch.Elapsed.TotalSeconds,
            Runtime = RuntimeInformation.FrameworkDescription,
            History = result.History
        };
    }

    private static DatasetSample[] SelectSplit(Dataset dataset, DatasetSplit split) =>
        dataset.Samples.Where(sample => sample.Split == split).ToArray();

    private static int FindMajorityClass(DatasetSample[] train) =>
        train.GroupBy(sample => sample.LabelIndex)
            .OrderByDescending(group => group.Count()).ThenBy(group => group.Key).First().Key;

    private static double MajorityAccuracy(DatasetSample[] samples, int majority) =>
        (double)samples.Count(sample => sample.LabelIndex == majority) / samples.Length;

    private static void DescribeDataset(Dataset dataset, TextWriter output)
    {
        output.WriteLine($"Classes: {dataset.Labels.Length}; images: {dataset.Samples.Length}; ignored files: {dataset.IgnoredFiles}");
        if (!dataset.CompleteGroupingMetadata)
        {
            output.WriteLine("Warning: grouping metadata is incomplete; only identical-file leakage is checked.");
        }
    }

    public static void ValidateOptions(TrainingOptions options)
    {
        if (options.Epochs is < 1 or > 10_000)
        {
            throw new ArgumentException("epochs must be between 1 and 10000.");
        }
        if (options.HiddenSize is < 1 or > 2048)
        {
            throw new ArgumentException("hidden-size must be between 1 and 2048.");
        }
        if (!double.IsFinite(options.LearningRate) || options.LearningRate is <= 0 or > 10)
        {
            throw new ArgumentException("learning-rate must be finite, greater than 0 and at most 10.");
        }
    }
}

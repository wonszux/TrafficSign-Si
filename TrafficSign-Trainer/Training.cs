using TrafficSign.Core;

namespace TrafficSign.Trainer;

/// <summary>Coordinates deterministic training, validation-based model selection and held-out evaluation.</summary>
public static class Training
{
    private sealed record TrainingResult(ModelData Model, int BestEpoch, List<EpochMetrics> History);

    /// <summary>Trains a model and publishes its model, split and report without overwriting existing outputs.</summary>
    /// <remarks>The best epoch minimizes validation loss. The test split is evaluated once, after restoring the selected model.</remarks>
    public static void Run(string directory, string modelPath, string? manifestPath, TrainingOptions options, TextWriter output)
    {
        ValidateOptions(options);
        ArgumentNullException.ThrowIfNull(output);
        var paths = ExperimentPaths.ForModel(modelPath);
        paths.EnsureTrainingOutputsAvailable();
        var dataset = Dataset.Load(directory, manifestPath, options.Seed, output);
        DescribeDataset(dataset, output);

        var trainingSamples = SelectSplit(dataset, DatasetSplit.Train);
        var validationSamples = SelectSplit(dataset, DatasetSplit.Validation);
        var result = Train(dataset.Labels, trainingSamples, validationSamples, options, output);
        var report = BuildTrainingReport(dataset, trainingSamples, validationSamples, options, result);
        ExperimentFiles.PublishTraining(paths, result.Model, dataset.ToManifest(), report, output);

        output.WriteLine($"Saved best epoch {result.BestEpoch}: {paths.Model}");
        output.WriteLine(FormattableString.Invariant($"Test accuracy={report.Test.Accuracy:P2}; loss={report.Test.Loss:F6}; n={report.Test.Count}"));
    }

    /// <summary>Computes average cross-entropy, accuracy, per-class recall and a confusion matrix.</summary>
    /// <param name="network">The network to evaluate without training.</param>
    /// <param name="samples">A nonempty split whose label indices match the model's label order.</param>
    /// <param name="classCount">Number of classes in the model.</param>
    /// <returns>A matrix with actual classes in rows and predicted classes in columns. Recall is zero for an absent class.</returns>
    public static Metrics Evaluate(NeuralNetwork network, DatasetSample[] samples, int classCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(classCount);
        var confusionMatrix = Enumerable.Range(0, classCount).Select(_ => new int[classCount]).ToArray();
        var summary = Measure(network, samples, classCount, confusionMatrix);
        var recallPerClass = confusionMatrix.Select((row, classIndex) =>
        {
            int actualCount = row.Sum();
            return actualCount == 0 ? 0 : (double)row[classIndex] / actualCount;
        }).ToArray();
        return new Metrics(samples.Length, summary.Loss, summary.Accuracy, recallPerClass, confusionMatrix);
    }

    private static LossAccuracy Measure(NeuralNetwork network, DatasetSample[] samples, int classCount, int[][]? confusionMatrix = null)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(classCount);
        if (samples.Length == 0)
        {
            throw new InvalidDataException("Cannot evaluate an empty split.");
        }
        double totalLoss = 0;
        int correctCount = 0;
        foreach (var sample in samples)
        {
            var evaluation = network.EvaluateSample(sample.Inputs, sample.LabelIndex);
            if (confusionMatrix is not null)
            {
                confusionMatrix[sample.LabelIndex][evaluation.PredictedIndex]++;
            }
            if (evaluation.PredictedIndex == sample.LabelIndex)
            {
                correctCount++;
            }
            totalLoss += evaluation.Loss;
        }
        double averageLoss = totalLoss / samples.Length;
        if (!double.IsFinite(averageLoss))
        {
            throw new ArithmeticException("Evaluation produced a non-finite loss.");
        }
        return new LossAccuracy(averageLoss, (double)correctCount / samples.Length);
    }

    private static TrainingResult Train(
        string[] labels, DatasetSample[] trainingSamples, DatasetSample[] validationSamples, TrainingOptions options, TextWriter output)
    {
        var network = NeuralNetwork.Create(options.HiddenSize, labels, options.Seed);
        var random = new Random(options.Seed);
        double bestValidationLoss = double.PositiveInfinity;
        int bestEpoch = 0;
        ModelData? bestModel = null;
        var history = new List<EpochMetrics>();
        for (int epoch = 1; epoch <= options.Epochs; epoch++)
        {
            random.Shuffle(trainingSamples);
            foreach (var sample in trainingSamples)
            {
                network.TrainSample(sample.Inputs, sample.LabelIndex, options.LearningRate);
            }
            var trainingMetrics = Measure(network, trainingSamples, labels.Length);
            var validationMetrics = Measure(network, validationSamples, labels.Length);
            output.WriteLine(FormattableString.Invariant(
                $"Epoch {epoch}/{options.Epochs}: train loss={trainingMetrics.Loss:F6} accuracy={trainingMetrics.Accuracy:P2}; validation loss={validationMetrics.Loss:F6} accuracy={validationMetrics.Accuracy:P2}"));
            history.Add(new EpochMetrics(epoch, trainingMetrics, validationMetrics));
            if (validationMetrics.Loss < bestValidationLoss)
            {
                bestValidationLoss = validationMetrics.Loss;
                bestEpoch = epoch;
                bestModel = network.GetModel();
            }
        }
        return new TrainingResult(
            bestModel ?? throw new InvalidOperationException("Training completed without selecting a model."), bestEpoch, history);
    }

    private static TrainingReport BuildTrainingReport(
        Dataset dataset, DatasetSample[] trainingSamples, DatasetSample[] validationSamples,
        TrainingOptions options, TrainingResult result)
    {
        var selectedNetwork = new NeuralNetwork(result.Model);
        var selectedTrain = Evaluate(selectedNetwork, trainingSamples, dataset.Labels.Length);
        var selectedValidation = Evaluate(selectedNetwork, validationSamples, dataset.Labels.Length);
        return new TrainingReport
        {
            Labels = dataset.Labels,
            Options = options,
            BestEpoch = result.BestEpoch,
            ClassCounts = dataset.Labels.Select((label, index) => new ClassCounts(
                label,
                trainingSamples.Count(sample => sample.LabelIndex == index),
                validationSamples.Count(sample => sample.LabelIndex == index),
                dataset.Samples.Count(sample => sample.LabelIndex == index && sample.Split == DatasetSplit.Test))).ToArray(),
            SelectedTrain = selectedTrain,
            SelectedValidation = selectedValidation,
            Test = Evaluate(selectedNetwork, SelectSplit(dataset, DatasetSplit.Test), dataset.Labels.Length),
            History = result.History
        };
    }

    private static DatasetSample[] SelectSplit(Dataset dataset, DatasetSplit split) =>
        dataset.Samples.Where(sample => sample.Split == split).ToArray();

    private static void DescribeDataset(Dataset dataset, TextWriter output) =>
        output.WriteLine($"Classes: {dataset.Labels.Length}; images: {dataset.Samples.Length}; ignored files: {dataset.IgnoredFiles}");

    /// <summary>Checks the supported epoch, hidden layer and learning rate ranges before loading any data.</summary>
    public static void ValidateOptions(TrainingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
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

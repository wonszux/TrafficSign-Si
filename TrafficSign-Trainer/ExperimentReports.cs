namespace TrafficSign.Trainer;

/// <summary>SGD settings; the seed controls both parameter initialization and training sample shuffling.</summary>
public sealed record TrainingOptions(int Epochs = 30, double LearningRate = 0.01, int HiddenSize = 64, int Seed = 42);
/// <summary>Split metrics. Confusion matrix rows are actual classes, columns are predicted classes.</summary>
public sealed record Metrics(int Count, double Loss, double Accuracy, double[] RecallPerClass, int[][] ConfusionMatrix);
public sealed record LossAccuracy(double Loss, double Accuracy);
public sealed record EpochMetrics(int Epoch, LossAccuracy Train, LossAccuracy Validation);

internal sealed record ClassCounts(string Label, int Train, int Validation, int Test);

internal sealed record TrainingReport
{
    public int FormatVersion { get; init; } = 2;
    public required string[] Labels { get; init; }
    public required TrainingOptions Options { get; init; }
    public required int BestEpoch { get; init; }
    public required ClassCounts[] ClassCounts { get; init; }
    public required Metrics SelectedTrain { get; init; }
    public required Metrics SelectedValidation { get; init; }
    public required Metrics Test { get; init; }
    public required List<EpochMetrics> History { get; init; }
}

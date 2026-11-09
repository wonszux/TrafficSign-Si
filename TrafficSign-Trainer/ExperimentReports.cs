namespace TrafficSign.Trainer;

/// <summary>SGD settings; the seed controls both parameter initialization and training sample shuffling.</summary>
public sealed record TrainingOptions(int Epochs = 30, double LearningRate = 0.01, int HiddenSize = 64, int Seed = 42);
/// <summary>Split metrics. Confusion matrix rows are actual classes, columns are predicted classes.</summary>
public sealed record Metrics(int Count, double Loss, double Accuracy, double[] RecallPerClass, int[][] ConfusionMatrix);
public sealed record EpochMetrics(int Epoch, Metrics Train, Metrics Validation);

internal sealed record ClassCounts(string Label, int Train, int Validation, int Test);
internal sealed record ValidationBaseline(string Label, double ValidationAccuracy);
internal sealed record TestBaseline(string Label, double TestAccuracy);

internal sealed record TrainingReport
{
    public int FormatVersion { get; init; } = 1;
    public string ModelSha256 { get; init; } = string.Empty;
    public required string DatasetFingerprint { get; init; }
    public required string[] Labels { get; init; }
    public required TrainingOptions Options { get; init; }
    public required int BestEpoch { get; init; }
    public required bool CompleteGroupingMetadata { get; init; }
    public string? GroupingLimitation { get; init; }
    public required ClassCounts[] ClassCounts { get; init; }
    public required ValidationBaseline Baseline { get; init; }
    public required Metrics SelectedTrain { get; init; }
    public required Metrics SelectedValidation { get; init; }
    public bool TestEvaluated { get; init; }
    public required double ElapsedSeconds { get; init; }
    public required string Runtime { get; init; }
    public required List<EpochMetrics> History { get; init; }
}

internal sealed record TestReport
{
    public int FormatVersion { get; init; } = 1;
    public required string ModelSha256 { get; init; }
    public required string DatasetFingerprint { get; init; }
    public required string[] Labels { get; init; }
    public bool TestEvaluated { get; init; } = true;
    public required Metrics Test { get; init; }
    public required bool CompleteGroupingMetadata { get; init; }
    public required TestBaseline Baseline { get; init; }
}

// Only these fields are needed to verify an existing v1 experiment.
internal sealed record TrainingReportIdentity
{
    public required int FormatVersion { get; init; }
    public required string ModelSha256 { get; init; }
    public required string DatasetFingerprint { get; init; }
}

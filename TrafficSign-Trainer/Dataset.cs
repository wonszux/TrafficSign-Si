namespace TrafficSign.Trainer;

public sealed class SplitManifest
{
    public required int FormatVersion { get; init; }
    public required SplitEntry[] Entries { get; init; }
}

public sealed class SplitEntry
{
    public required string Path { get; init; }
    public string? Split { get; set; }
    public string? GroupId { get; init; }
    public string? Sha256 { get; init; }
}

public enum DatasetSplit { Train, Validation, Test }

internal static class DatasetSplits
{
    public static string ToName(this DatasetSplit split) => split switch
    {
        DatasetSplit.Train => "train",
        DatasetSplit.Validation => "validation",
        DatasetSplit.Test => "test",
        _ => throw new ArgumentOutOfRangeException(nameof(split))
    };

    public static DatasetSplit? Parse(string? value) => value switch
    {
        null => null,
        "train" => DatasetSplit.Train,
        "validation" => DatasetSplit.Validation,
        "test" => DatasetSplit.Test,
        _ => throw new InvalidDataException($"Invalid split '{value}'. Use train, validation, test or null.")
    };
}

public sealed record DatasetSample(string RelativePath, int LabelIndex, string Sha256,
    string? GroupId, DatasetSplit Split, double[] Inputs);

public sealed class Dataset
{
    public required string[] Labels { get; init; }
    public required DatasetSample[] Samples { get; init; }
    public required string Fingerprint { get; init; }
    public required bool CompleteGroupingMetadata { get; init; }
    public required int IgnoredFiles { get; init; }

    public SplitManifest ToManifest() => new()
    {
        FormatVersion = 1,
        Entries = Samples.Select(sample => new SplitEntry
        {
            Path = sample.RelativePath,
            Split = sample.Split.ToName(),
            GroupId = sample.GroupId,
            Sha256 = sample.Sha256
        }).ToArray()
    };

    public static Dataset Load(string directory, string? manifestPath, int seed, bool requireCompleteSplit = false) =>
        DatasetLoader.Load(directory, manifestPath, seed, requireCompleteSplit);
}

namespace TrafficSign.Trainer;

/// <summary>The versioned JSON contract for grouping images and preserving an experiment's exact splits.</summary>
public sealed class SplitManifest
{
    public required int FormatVersion { get; init; }
    public required SplitEntry[] Entries { get; init; }
}

/// <summary>Metadata for one image, using a path relative to the dataset directory.</summary>
public sealed class SplitEntry
{
    public required string Path { get; init; }
    public string? Split { get; set; }
    public string? GroupId { get; init; }
    public string? Sha256 { get; init; }
}

public enum DatasetSplit
{
    Train,
    Validation,
    Test
}

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

/// <summary>A decoded image with its class index, identity, grouping metadata and assigned split.</summary>
public sealed record DatasetSample(string RelativePath, int LabelIndex, string Sha256,
    string? GroupId, DatasetSplit Split, double[] Inputs);

/// <summary>A validated, in-memory dataset ordered by class and relative image path.</summary>
public sealed class Dataset
{
    public required string[] Labels { get; init; }
    public required DatasetSample[] Samples { get; init; }
    /// <summary>A SHA256 of the ordered image identities, labels, groups and splits, using the v1 representation.</summary>
    public required string Fingerprint { get; init; }
    /// <summary>Whether every image has an explicit group identifier. False means only identical copies are detected.</summary>
    public required bool CompleteGroupingMetadata { get; init; }
    public required int IgnoredFiles { get; init; }

    /// <summary>Exports the exact split and image hashes for later evaluation against unchanged data.</summary>
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

    /// <summary>Discovers, validates, groups, partitions and decodes class-directory JPEG/PNG images.</summary>
    /// <param name="directory">The directory whose immediate subdirectories name the classes.</param>
    /// <param name="manifestPath">Optional grouping or split manifest; null requests automatic splitting.</param>
    /// <param name="seed">Seed for group shuffling. Ignored when the manifest provides all three splits.</param>
    /// <param name="requireCompleteSplit">Require an exact train/validation/test assignment and a hash for every image.</param>
    public static Dataset Load(string directory, string? manifestPath, int seed, bool requireCompleteSplit = false) =>
        DatasetLoader.Load(directory, manifestPath, seed, requireCompleteSplit);
}

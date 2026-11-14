namespace TrafficSign.Trainer;

/// <summary>The versioned JSON contract for preserving image split assignments.</summary>
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

/// <summary>A decoded image with its class index, relative path and assigned split.</summary>
public sealed record DatasetSample(string RelativePath, int LabelIndex, DatasetSplit Split, double[] Inputs);

/// <summary>A validated, in-memory dataset ordered by class and relative image path.</summary>
public sealed class Dataset
{
    public required string[] Labels { get; init; }
    public required DatasetSample[] Samples { get; init; }
    public required int IgnoredFiles { get; init; }

    /// <summary>Exports the split assignments without checking image contents.</summary>
    public SplitManifest ToManifest() => new()
    {
        FormatVersion = 2,
        Entries = Samples.Select(sample => new SplitEntry
        {
            Path = sample.RelativePath,
            Split = sample.Split.ToName()
        }).ToArray()
    };

    /// <summary>Discovers, validates, partitions and decodes class-directory JPEG/PNG images.</summary>
    /// <param name="directory">The directory whose immediate subdirectories name the classes.</param>
    /// <param name="manifestPath">Optional split manifest; null requests automatic splitting.</param>
    /// <param name="seed">Seed for file shuffling. Ignored when all three splits are supplied.</param>
    /// <param name="output">Receives compatibility notices; defaults to standard output.</param>
    public static Dataset Load(string directory, string? manifestPath, int seed, TextWriter? output = null) =>
        DatasetLoader.Load(directory, manifestPath, seed, output ?? Console.Out);
}

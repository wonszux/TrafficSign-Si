using System.Text.Json;
using TrafficSign.Core;

namespace TrafficSign.Trainer;

internal static class DatasetLoader
{
    private const int MaximumImages = 20_000;
    private const double HeldOutSplitFraction = 0.15;

    // One entry owns all metadata for an image while its split is being prepared.
    private sealed class PreparedImage(string fullPath, string relativePath, int labelIndex)
    {
        public string FullPath { get; } = fullPath;
        public string RelativePath { get; } = relativePath;
        public int LabelIndex { get; } = labelIndex;
        public DatasetSplit? Split { get; set; }
    }

    public static Dataset Load(string directory, string? manifestPath, int seed, TextWriter output)
    {
        var (labels, images, ignoredFiles) = DiscoverImages(directory);
        var manifest = LoadManifest(manifestPath, images, output);
        foreach (var image in images)
        {
            image.Split = DatasetSplits.Parse(manifest?.GetValueOrDefault(image.RelativePath)?.Split);
        }
        AssignSplits(images, labels, seed);
        ValidateSplitCoverage(images, labels);
        var samples = DecodeSamples(images);

        return new Dataset
        {
            Labels = labels,
            Samples = samples,
            IgnoredFiles = ignoredFiles
        };
    }

    private static (string[] Labels, PreparedImage[] Images, int IgnoredFiles) DiscoverImages(string directory)
    {
        string root = Path.GetFullPath(directory);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Dataset directory does not exist: {root}");
        }

        string[] classDirectories = Directory.GetDirectories(root).Order(StringComparer.Ordinal).ToArray();
        string[] labels = classDirectories.Select(path => Path.GetFileName(path)).ToArray();
        if (labels.Length < 2)
        {
            throw new InvalidDataException("Dataset requires at least two class directories.");
        }

        var images = new List<PreparedImage>();
        int ignoredFiles = Directory.GetFiles(root).Length;
        for (int labelIndex = 0; labelIndex < labels.Length; labelIndex++)
        {
            string[] candidates = Directory.GetFiles(classDirectories[labelIndex]).Order(StringComparer.Ordinal).ToArray();
            ignoredFiles += candidates.Count(path => !ImagePreprocessor.IsSupportedPath(path));
            string[] supportedPaths = candidates.Where(ImagePreprocessor.IsSupportedPath).ToArray();
            if (supportedPaths.Length == 0)
            {
                throw new InvalidDataException($"Class '{labels[labelIndex]}' contains no JPEG/PNG images.");
            }
            if (Directory.GetDirectories(classDirectories[labelIndex]).Length > 0)
            {
                throw new InvalidDataException($"Nested image directories are unsupported: {classDirectories[labelIndex]}");
            }
            images.AddRange(supportedPaths.Select(path =>
                new PreparedImage(path, Path.GetRelativePath(root, path).Replace('\\', '/'), labelIndex)));
        }

        // Eager decoding is deliberately limited to a small experiment (~160 MiB of vectors).
        if (images.Count > MaximumImages)
        {
            throw new InvalidDataException("Dataset exceeds 20,000 images. Select a smaller experiment.");
        }
        return (labels, images.ToArray(), ignoredFiles);
    }

    private static Dictionary<string, SplitEntry>? LoadManifest(
        string? path, PreparedImage[] images, TextWriter output)
    {
        if (path is null)
        {
            return null;
        }

        using var stream = File.OpenRead(path);
        SplitManifest manifest;
        bool hasLegacyMetadata;
        try
        {
            using var document = JsonDocument.Parse(stream);
            manifest = document.RootElement.Deserialize<SplitManifest>(ModelFile.JsonOptions)
                ?? throw new InvalidDataException("Split manifest cannot be null.");
            hasLegacyMetadata = manifest.FormatVersion == 1 &&
                document.RootElement.GetProperty("entries").ValueKind == JsonValueKind.Array &&
                document.RootElement.GetProperty("entries").EnumerateArray().Any(entry =>
                    entry.ValueKind == JsonValueKind.Object &&
                    (entry.TryGetProperty("sha256", out _) || entry.TryGetProperty("groupId", out _)));
        }
        catch (JsonException failure)
        {
            throw new JsonException($"Invalid split manifest: {failure.Message}", failure);
        }
        if (manifest.FormatVersion is not (1 or 2) || manifest.Entries is null)
        {
            throw new InvalidDataException("Unsupported split manifest.");
        }

        var entries = new Dictionary<string, SplitEntry>(StringComparer.Ordinal);
        foreach (var entry in manifest.Entries)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.Path) || Path.IsPathRooted(entry.Path) ||
                entry.Path.Split('/', '\\').Any(part => part is "." or ".."))
            {
                throw new InvalidDataException("Manifest entries require safe relative image paths.");
            }
            string normalizedPath = entry.Path.Replace('\\', '/');
            if (!entries.TryAdd(normalizedPath, entry))
            {
                throw new InvalidDataException($"Duplicate manifest path: {normalizedPath}");
            }
            DatasetSplits.Parse(entry.Split);
        }
        if (entries.Count != images.Length || images.Any(image => !entries.ContainsKey(image.RelativePath)))
        {
            throw new InvalidDataException("Manifest must describe every dataset image exactly once.");
        }
        if (hasLegacyMetadata)
        {
            output.WriteLine("Notice: legacy manifest v1 sha256/groupId metadata is ignored; image contents are not verified or grouped.");
        }
        return entries;
    }

    private static void AssignSplits(PreparedImage[] images, string[] labels, int seed)
    {
        bool anyAssigned = images.Any(image => image.Split.HasValue);
        bool needsValidationSplit = anyAssigned && !images.Any(image => image.Split == DatasetSplit.Validation);
        if (anyAssigned && images.Any(image => !image.Split.HasValue))
        {
            throw new InvalidDataException("Supply splits for all images, or leave all unassigned.");
        }

        var random = new Random(seed);
        for (int labelIndex = 0; labelIndex < labels.Length; labelIndex++)
        {
            var classImages = images.Where(image => image.LabelIndex == labelIndex).ToArray();
            if (!anyAssigned)
            {
                AssignAutomaticSplits(classImages, labels[labelIndex], random);
            }
            else if (needsValidationSplit)
            {
                ReserveValidation(classImages, labels[labelIndex], random);
            }
        }
    }

    private static void AssignAutomaticSplits(PreparedImage[] images, string label, Random random)
    {
        if (images.Length < 3)
        {
            throw new InvalidDataException($"Class '{label}' needs at least three files.");
        }
        random.Shuffle(images);
        int validationCount = Math.Clamp((int)Math.Round(images.Length * HeldOutSplitFraction), 1, images.Length - 2);
        int testCount = Math.Clamp((int)Math.Round(images.Length * HeldOutSplitFraction), 1, images.Length - validationCount - 1);
        for (int i = 0; i < images.Length; i++)
        {
            var split = i < testCount ? DatasetSplit.Test
                : i < testCount + validationCount ? DatasetSplit.Validation : DatasetSplit.Train;
            images[i].Split = split;
        }
    }

    private static void ReserveValidation(PreparedImage[] images, string label, Random random)
    {
        var trainingImages = images.Where(image => image.Split == DatasetSplit.Train).ToArray();
        if (trainingImages.Length < 2)
        {
            throw new InvalidDataException($"Class '{label}' needs at least two training files to reserve validation.");
        }
        random.Shuffle(trainingImages);
        // Reserve 15 of the nominal 85 non-test percent without changing the supplied test split.
        int validationCount = Math.Clamp((int)Math.Round(trainingImages.Length * 15.0 / 85), 1, trainingImages.Length - 1);
        foreach (var image in trainingImages.Take(validationCount))
        {
            image.Split = DatasetSplit.Validation;
        }
    }

    private static void ValidateSplitCoverage(PreparedImage[] images, string[] labels)
    {
        for (int labelIndex = 0; labelIndex < labels.Length; labelIndex++)
        {
            foreach (var split in Enum.GetValues<DatasetSplit>())
            {
                if (!images.Any(image => image.LabelIndex == labelIndex && image.Split == split))
                {
                    throw new InvalidDataException($"Class '{labels[labelIndex]}' has no samples in {split.ToName()}.");
                }
            }
        }
    }

    private static DatasetSample[] DecodeSamples(PreparedImage[] images) => images.Select(image =>
        new DatasetSample(image.RelativePath, image.LabelIndex,
            image.Split ?? throw new InvalidOperationException($"Image has no assigned split: {image.RelativePath}"),
            ImagePreprocessor.Process(image.FullPath))).ToArray();
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TrafficSign.Core;

namespace TrafficSign.Trainer;

internal static class DatasetLoader
{
    // One entry owns all metadata for an image while its split is being prepared.
    private sealed class PreparedImage(string fullPath, string relativePath, int labelIndex)
    {
        public string FullPath { get; } = fullPath;
        public string RelativePath { get; } = relativePath;
        public int LabelIndex { get; } = labelIndex;
        public string Sha256 { get; set; } = "";
        public string? GroupId { get; set; }
        public DatasetSplit? Split { get; set; }
    }

    public static Dataset Load(string directory, string? manifestPath, int seed, bool requireCompleteSplit)
    {
        var (labels, images, ignoredFiles) = DiscoverImages(directory);
        var manifest = LoadManifest(manifestPath, images, requireCompleteSplit);
        ReadMetadata(images, manifest);
        var groups = BuildIndependentGroups(images);
        AssignSplits(groups, labels, seed, requireCompleteSplit);
        ValidateSplitCoverage(images, labels);
        var samples = DecodeSamples(images);

        return new Dataset
        {
            Labels = labels,
            Samples = samples,
            Fingerprint = ComputeFingerprint(samples),
            CompleteGroupingMetadata = images.All(image => image.GroupId is not null),
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
        int ignored = Directory.GetFiles(root).Length;
        for (int label = 0; label < labels.Length; label++)
        {
            string[] candidates = Directory.GetFiles(classDirectories[label]).Order(StringComparer.Ordinal).ToArray();
            ignored += candidates.Count(path => !ImagePreprocessor.IsSupportedPath(path));
            string[] supported = candidates.Where(ImagePreprocessor.IsSupportedPath).ToArray();
            if (supported.Length == 0)
            {
                throw new InvalidDataException($"Class '{labels[label]}' contains no JPEG/PNG images.");
            }
            if (Directory.GetDirectories(classDirectories[label]).Length > 0)
            {
                throw new InvalidDataException($"Nested image directories are unsupported: {classDirectories[label]}");
            }
            images.AddRange(supported.Select(path =>
                new PreparedImage(path, Path.GetRelativePath(root, path).Replace('\\', '/'), label)));
        }

        // Eager decoding is deliberately limited to a small experiment (~160 MiB of vectors).
        if (images.Count > 20_000)
        {
            throw new InvalidDataException("Dataset exceeds 20,000 images. Select a smaller experiment.");
        }
        return (labels, images.ToArray(), ignored);
    }

    private static Dictionary<string, SplitEntry>? LoadManifest(
        string? path, PreparedImage[] images, bool requireCompleteSplit)
    {
        if (path is null)
        {
            if (requireCompleteSplit)
            {
                throw new InvalidDataException("Evaluation requires a saved split manifest.");
            }
            return null;
        }

        using var stream = File.OpenRead(path);
        SplitManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<SplitManifest>(stream, ModelFile.JsonOptions)
                ?? throw new InvalidDataException("Split manifest cannot be null.");
        }
        catch (JsonException failure)
        {
            throw new JsonException($"Invalid split manifest: {failure.Message}", failure);
        }
        if (manifest.FormatVersion != 1 || manifest.Entries is null)
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
            string key = entry.Path.Replace('\\', '/');
            if (!entries.TryAdd(key, entry))
            {
                throw new InvalidDataException($"Duplicate manifest path: {key}");
            }
            if (requireCompleteSplit && entry.Sha256 is null)
            {
                throw new InvalidDataException("Saved evaluation split manifest must contain SHA256 for every image.");
            }
            DatasetSplits.Parse(entry.Split);
        }
        if (entries.Count != images.Length || images.Any(image => !entries.ContainsKey(image.RelativePath)))
        {
            throw new InvalidDataException("Manifest must describe every dataset image exactly once.");
        }
        return entries;
    }

    private static void ReadMetadata(PreparedImage[] images, Dictionary<string, SplitEntry>? manifest)
    {
        foreach (var image in images)
        {
            using var stream = File.OpenRead(image.FullPath);
            image.Sha256 = Convert.ToHexString(SHA256.HashData(stream));
            var entry = manifest?.GetValueOrDefault(image.RelativePath);
            if (entry?.Sha256 is not null && !image.Sha256.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Image changed since split was saved: {image.RelativePath}");
            }
            image.GroupId = string.IsNullOrWhiteSpace(entry?.GroupId) ? null : entry.GroupId;
            image.Split = DatasetSplits.Parse(entry?.Split);
        }
    }

    private static PreparedImage[][] BuildIndependentGroups(PreparedImage[] images)
    {
        int[] parents = Enumerable.Range(0, images.Length).ToArray();
        var hashOwners = new Dictionary<string, int>(StringComparer.Ordinal);
        var groupOwners = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < images.Length; i++)
        {
            var image = images[i];
            if (hashOwners.TryGetValue(image.Sha256, out int sameImage))
            {
                if (images[sameImage].LabelIndex != image.LabelIndex)
                {
                    throw new InvalidDataException($"Identical image has conflicting labels: {image.RelativePath}");
                }
                Union(sameImage, i);
            }
            else
            {
                hashOwners[image.Sha256] = i;
            }
            if (image.GroupId is { } group)
            {
                if (groupOwners.TryGetValue(group, out int sameGroup))
                {
                    Union(sameGroup, i);
                }
                else
                {
                    groupOwners[group] = i;
                }
            }
        }

        var groups = Enumerable.Range(0, images.Length).GroupBy(Find)
            .Select(group => group.Select(index => images[index]).ToArray()).ToArray();
        foreach (var group in groups)
        {
            if (group.Select(image => image.LabelIndex).Distinct().Count() > 1)
            {
                throw new InvalidDataException("A group spans several labels. This trainer requires groups of one sign class.");
            }
            if (group.Where(image => image.Split.HasValue).Select(image => image.Split).Distinct().Count() > 1)
            {
                throw new InvalidDataException($"A duplicate image or group crosses splits: {group[0].RelativePath}");
            }
        }
        return groups;

        int Find(int value)
        {
            while (parents[value] != value)
            {
                parents[value] = parents[parents[value]];
                value = parents[value];
            }
            return value;
        }

        void Union(int a, int b) => parents[Find(b)] = Find(a);
    }

    private static void AssignSplits(PreparedImage[][] groups, string[] labels, int seed, bool requireCompleteSplit)
    {
        var images = groups.SelectMany(group => group).ToArray();
        bool anyAssigned = images.Any(image => image.Split.HasValue);
        bool reserveValidation = anyAssigned && !images.Any(image => image.Split == DatasetSplit.Validation);
        if (anyAssigned && images.Any(image => !image.Split.HasValue))
        {
            throw new InvalidDataException("Supply splits for all images, or leave all unassigned.");
        }
        if (requireCompleteSplit && (!anyAssigned || reserveValidation))
        {
            throw new InvalidDataException("Evaluation requires the exact saved train/validation/test split.");
        }

        var random = new Random(seed);
        for (int label = 0; label < labels.Length; label++)
        {
            var classGroups = groups.Where(group => group[0].LabelIndex == label).ToArray();
            if (!anyAssigned)
            {
                AssignAutomaticSplits(classGroups, labels[label], random);
            }
            else if (reserveValidation)
            {
                ReserveValidation(classGroups, labels[label], random);
            }
        }
    }

    private static void AssignAutomaticSplits(PreparedImage[][] groups, string label, Random random)
    {
        if (groups.Length < 3)
        {
            throw new InvalidDataException($"Class '{label}' needs at least three independent images/groups.");
        }
        random.Shuffle(groups);
        int validationCount = Math.Clamp((int)Math.Round(groups.Length * 0.15), 1, groups.Length - 2);
        int testCount = Math.Clamp((int)Math.Round(groups.Length * 0.15), 1, groups.Length - validationCount - 1);
        for (int i = 0; i < groups.Length; i++)
        {
            var split = i < testCount ? DatasetSplit.Test
                : i < testCount + validationCount ? DatasetSplit.Validation : DatasetSplit.Train;
            foreach (var image in groups[i])
            {
                image.Split = split;
            }
        }
    }

    private static void ReserveValidation(PreparedImage[][] groups, string label, Random random)
    {
        var trainingGroups = groups.Where(group => group[0].Split == DatasetSplit.Train).ToArray();
        if (trainingGroups.Length < 2)
        {
            throw new InvalidDataException($"Class '{label}' needs at least two training groups to reserve validation.");
        }
        random.Shuffle(trainingGroups);
        int count = Math.Clamp((int)Math.Round(trainingGroups.Length * 15.0 / 85), 1, trainingGroups.Length - 1);
        foreach (var group in trainingGroups.Take(count))
        {
            foreach (var image in group)
            {
                image.Split = DatasetSplit.Validation;
            }
        }
    }

    private static void ValidateSplitCoverage(PreparedImage[] images, string[] labels)
    {
        for (int label = 0; label < labels.Length; label++)
        {
            foreach (var split in Enum.GetValues<DatasetSplit>())
            {
                if (!images.Any(image => image.LabelIndex == label && image.Split == split))
                {
                    throw new InvalidDataException($"Class '{labels[label]}' has no independent samples in {split.ToName()}.");
                }
            }
        }
    }

    private static DatasetSample[] DecodeSamples(PreparedImage[] images) => images.Select(image =>
        new DatasetSample(image.RelativePath, image.LabelIndex, image.Sha256, image.GroupId,
            image.Split!.Value, ImagePreprocessor.Process(image.FullPath))).ToArray();

    private static string ComputeFingerprint(DatasetSample[] samples)
    {
        // v1 fingerprints use this compact PascalCase representation, not manifest JSON options.
        string description = JsonSerializer.Serialize(samples.Select(sample => new
        {
            sample.RelativePath,
            sample.LabelIndex,
            sample.Sha256,
            sample.GroupId,
            Split = sample.Split.ToName()
        }));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(description)));
    }
}

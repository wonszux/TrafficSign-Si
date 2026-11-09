using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TrafficSign.Core;

namespace TrafficSign.Trainer;

public sealed class SplitManifest
{
    public int FormatVersion { get; init; } = 1;
    public required SplitEntry[] Entries { get; init; }
}

public sealed class SplitEntry
{
    public required string Path { get; init; }
    public string? Split { get; set; }
    public string? GroupId { get; init; }
    public string? Sha256 { get; init; }
}

public sealed record DatasetSample(string RelativePath, int LabelIndex, string Sha256,
    string? GroupId, string Split, double[] Inputs);

public sealed class Dataset
{
    public required string[] Labels { get; init; }
    public required DatasetSample[] Samples { get; init; }
    public required string Fingerprint { get; init; }
    public required bool CompleteGroupingMetadata { get; init; }
    public required int IgnoredFiles { get; init; }

    public SplitManifest ToManifest() => new()
    {
        Entries = Samples.Select(sample => new SplitEntry
        {
            Path = sample.RelativePath, Split = sample.Split,
            GroupId = sample.GroupId, Sha256 = sample.Sha256
        }).ToArray()
    };

    public static Dataset Load(string directory, string? manifestPath, int seed, bool requireCompleteSplit = false)
    {
        string root = System.IO.Path.GetFullPath(directory);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Dataset directory does not exist: {root}");
        string[] classDirectories = Directory.GetDirectories(root).Order(StringComparer.Ordinal).ToArray();
        string[] labels = classDirectories.Select(System.IO.Path.GetFileName).Select(x => x!).ToArray();
        if (labels.Length < 2) throw new InvalidDataException("Dataset requires at least two class directories.");
        var files = new List<(string FullPath, string RelativePath, int Label)>();
        int ignored = Directory.GetFiles(root).Length;
        for (int label = 0; label < labels.Length; label++)
        {
            string[] candidates = Directory.GetFiles(classDirectories[label]).Order(StringComparer.Ordinal).ToArray();
            ignored += candidates.Count(path => !ImagePreprocessor.IsSupportedPath(path));
            string[] images = candidates.Where(ImagePreprocessor.IsSupportedPath).ToArray();
            if (images.Length == 0) throw new InvalidDataException($"Class '{labels[label]}' contains no JPEG/PNG images.");
            if (Directory.GetDirectories(classDirectories[label]).Length > 0)
                throw new InvalidDataException($"Nested image directories are unsupported: {classDirectories[label]}");
            files.AddRange(images.Select(path => (path, System.IO.Path.GetRelativePath(root, path).Replace('\\', '/'), label)));
        }
        // Limit eager preprocessing to an explicit, small educational experiment (~160 MiB of vectors).
        if (files.Count > 20_000) throw new InvalidDataException("Dataset exceeds 20,000 images. Select a smaller experiment.");
        Dictionary<string, SplitEntry>? supplied = null;
        if (manifestPath is not null)
        {
            using var stream = File.OpenRead(manifestPath);
            var manifest = JsonSerializer.Deserialize<SplitManifest>(stream, ModelFile.JsonOptions)
                ?? throw new InvalidDataException("Split manifest cannot be null.");
            if (manifest.FormatVersion != 1 || manifest.Entries is null)
                throw new InvalidDataException("Unsupported split manifest.");
            supplied = new Dictionary<string, SplitEntry>(StringComparer.Ordinal);
            foreach (var entry in manifest.Entries)
            {
                if (entry is null || string.IsNullOrWhiteSpace(entry.Path) ||
                    System.IO.Path.IsPathRooted(entry.Path) || entry.Path.Split('/', '\\').Any(part => part is "." or ".."))
                    throw new InvalidDataException("Manifest entries require safe relative image paths.");
                string key = entry.Path.Replace('\\', '/');
                if (!supplied.TryAdd(key, entry)) throw new InvalidDataException($"Duplicate manifest path: {key}");
                if (requireCompleteSplit && entry.Sha256 is null)
                    throw new InvalidDataException("Saved evaluation split must contain SHA256 for every image.");
                if (entry.Split is not (null or "train" or "validation" or "test"))
                    throw new InvalidDataException($"Invalid split for {key}. Use train, validation, test or null.");
            }
            if (supplied.Count != files.Count || files.Any(file => !supplied.ContainsKey(file.RelativePath)))
                throw new InvalidDataException("Manifest must describe every dataset image exactly once.");
        }
        if (requireCompleteSplit && supplied is null) throw new InvalidDataException("Evaluation requires a saved split manifest.");
        int[] parent = Enumerable.Range(0, files.Count).ToArray();
        int Find(int value)
        {
            while (parent[value] != value) { parent[value] = parent[parent[value]]; value = parent[value]; }
            return value;
        }
        void Union(int a, int b) => parent[Find(b)] = Find(a);
        var hashes = new string[files.Count];
        var hashOwners = new Dictionary<string, int>(StringComparer.Ordinal);
        var groupOwners = new Dictionary<string, int>(StringComparer.Ordinal);
        var groups = new string?[files.Count];
        var splits = new string?[files.Count];
        for (int i = 0; i < files.Count; i++)
        {
            using var image = File.OpenRead(files[i].FullPath);
            hashes[i] = Convert.ToHexString(SHA256.HashData(image));
            var entry = supplied?.GetValueOrDefault(files[i].RelativePath);
            if (entry?.Sha256 is not null && !hashes[i].Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Image changed since split was saved: {files[i].RelativePath}");
            groups[i] = string.IsNullOrWhiteSpace(entry?.GroupId) ? null : entry.GroupId;
            splits[i] = entry?.Split;
            if (hashOwners.TryGetValue(hashes[i], out int sameImage))
            {
                if (files[sameImage].Label != files[i].Label)
                    throw new InvalidDataException($"Identical image has conflicting labels: {files[i].RelativePath}");
                Union(sameImage, i);
            }
            else hashOwners[hashes[i]] = i;
            if (groups[i] is { } group)
            {
                if (groupOwners.TryGetValue(group, out int sameGroup)) Union(sameGroup, i);
                else groupOwners[group] = i;
            }
        }
        var units = Enumerable.Range(0, files.Count).GroupBy(Find).Select(group => group.ToArray()).ToArray();
        foreach (int[] unit in units)
        {
            if (unit.Select(i => files[i].Label).Distinct().Count() > 1)
                throw new InvalidDataException("A group spans several labels. This trainer requires groups of one sign class.");
            if (unit.Select(i => splits[i]).Where(x => x is not null).Distinct().Count() > 1)
                throw new InvalidDataException($"A duplicate image or group crosses splits: {files[unit[0]].RelativePath}");
        }
        bool anyAssigned = splits.Any(x => x is not null);
        bool reserveValidation = anyAssigned && !splits.Contains("validation");
        if (anyAssigned && splits.Any(x => x is null)) throw new InvalidDataException("Supply splits for all images, or leave all unassigned.");
        var random = new Random(seed);
        if (requireCompleteSplit && (!anyAssigned || !splits.Contains("validation")))
            throw new InvalidDataException("Evaluation requires the exact saved train/validation/test split.");
        for (int label = 0; label < labels.Length; label++)
        {
            int[][] classUnits = units.Where(unit => files[unit[0]].Label == label).ToArray();
            if (!anyAssigned)
            {
                if (classUnits.Length < 3) throw new InvalidDataException($"Class '{labels[label]}' needs at least three independent images/groups.");
                random.Shuffle(classUnits);
                int validationCount = Math.Clamp((int)Math.Round(classUnits.Length * 0.15), 1, classUnits.Length - 2);
                int testCount = Math.Clamp((int)Math.Round(classUnits.Length * 0.15), 1, classUnits.Length - validationCount - 1);
                for (int u = 0; u < classUnits.Length; u++)
                    foreach (int i in classUnits[u]) splits[i] = u < testCount ? "test" : u < testCount + validationCount ? "validation" : "train";
            }
            else if (reserveValidation)
            {
                int[][] trainingUnits = classUnits.Where(unit => splits[unit[0]] == "train").ToArray();
                if (trainingUnits.Length < 2) throw new InvalidDataException($"Class '{labels[label]}' needs at least two training groups to reserve validation.");
                random.Shuffle(trainingUnits);
                int count = Math.Clamp((int)Math.Round(trainingUnits.Length * 15.0 / 85), 1, trainingUnits.Length - 1);
                foreach (int[] unit in trainingUnits.Take(count)) foreach (int i in unit) splits[i] = "validation";
            }
        }
        for (int label = 0; label < labels.Length; label++)
            foreach (string split in new[] { "train", "validation", "test" })
                if (!Enumerable.Range(0, files.Count).Any(i => files[i].Label == label && splits[i] == split))
                    throw new InvalidDataException($"Class '{labels[label]}' has no independent samples in {split}.");
        var samples = files.Select((file, i) => new DatasetSample(file.RelativePath, file.Label,
            hashes[i], groups[i], splits[i]!, ImagePreprocessor.Process(file.FullPath))).ToArray();
        string description = JsonSerializer.Serialize(samples.Select(sample => new
        {
            sample.RelativePath, sample.LabelIndex, sample.Sha256, sample.GroupId, sample.Split
        }));
        return new Dataset
        {
            Labels = labels, Samples = samples, CompleteGroupingMetadata = groups.All(group => group is not null),
            Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(description))), IgnoredFiles = ignored
        };
    }
}

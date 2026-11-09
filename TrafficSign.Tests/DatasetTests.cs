using System.Text.Json;
using TrafficSign.Core;
using TrafficSign.Trainer;
using Xunit;

namespace TrafficSign.Tests;

public sealed class DatasetTests
{
    [Fact]
    public void SplitIsDeterministicAndGroupsStayTogether()
    {
        using var files = new TestFiles();
        string root = files.Dataset();
        var entries = Directory.GetFiles(root, "*.png", SearchOption.AllDirectories).Select(path =>
            new SplitEntry
            {
                Path = Path.GetRelativePath(root, path).Replace('\\', '/'),
                GroupId = Path.GetFileName(Path.GetDirectoryName(path)) + "/" + (int.Parse(Path.GetFileNameWithoutExtension(path)) / 2)
            }).ToArray();
        string manifest = files.PathFor("groups.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new SplitManifest { FormatVersion = 1, Entries = entries }, ModelFile.JsonOptions));
        var firstDataset = Dataset.Load(root, manifest, 42);
        var repeatedDataset = Dataset.Load(root, manifest, 42);
        Assert.Equal(firstDataset.Fingerprint, repeatedDataset.Fingerprint);
        Assert.True(firstDataset.CompleteGroupingMetadata);
        foreach (var group in firstDataset.Samples.GroupBy(sample => sample.GroupId))
        {
            Assert.Single(group.Select(sample => sample.Split).Distinct());
        }

        foreach (var split in Enum.GetValues<DatasetSplit>())
        {
            Assert.Equal(2, firstDataset.Samples.Where(sample => sample.Split == split).Select(sample => sample.LabelIndex).Distinct().Count());
        }

        Assert.Equal(firstDataset.Samples.Length, firstDataset.Samples.Select(sample => sample.RelativePath).Distinct().Count());
    }

    [Fact]
    public void OfficialTestIsPreservedAndValidationComesFromTrainingForEveryClass()
    {
        using var files = new TestFiles();
        string root = files.Dataset();
        var entries = Directory.GetFiles(root, "*.png", SearchOption.AllDirectories).Select(path =>
            new SplitEntry { Path = Path.GetRelativePath(root, path).Replace('\\', '/'), Split = Path.GetFileName(path) == "00.png" ? "test" : "train" }).ToArray();
        string manifest = files.PathFor("official.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new SplitManifest { FormatVersion = 1, Entries = entries }, ModelFile.JsonOptions));
        var dataset = Dataset.Load(root, manifest, 7);
        Assert.Equal(2, dataset.Samples.Count(sample => sample.Split == DatasetSplit.Test));
        Assert.All(dataset.Samples.Where(sample => sample.Split == DatasetSplit.Test), sample => Assert.EndsWith("00.png", sample.RelativePath));
        Assert.Equal(2, dataset.Samples.Where(sample => sample.Split == DatasetSplit.Validation).Select(sample => sample.LabelIndex).Distinct().Count());
        string saved = files.PathFor("saved.json");
        File.WriteAllText(saved, JsonSerializer.Serialize(dataset.ToManifest(), ModelFile.JsonOptions));
        Assert.Equal(dataset.Fingerprint, Dataset.Load(root, saved, 999, requireCompleteSplit: true).Fingerprint);
        File.AppendAllText(Path.Combine(root, "dark", "00.png"), "changed");
        Assert.Throws<InvalidDataException>(() => Dataset.Load(root, saved, 7, requireCompleteSplit: true));
    }

    [Fact]
    public void DuplicatesCannotCrossSplitOrLabels()
    {
        using var files = new TestFiles();
        string root = files.Dataset();
        File.Copy(Path.Combine(root, "dark", "00.png"), Path.Combine(root, "dark", "01.png"), overwrite: true);
        var automatic = Dataset.Load(root, null, 42);
        Assert.Single(automatic.Samples.Where(sample => sample.RelativePath is "dark/00.png" or "dark/01.png").Select(sample => sample.Split).Distinct());
        var entries = automatic.ToManifest().Entries;
        entries.Single(entry => entry.Path == "dark/00.png").Split = "train";
        entries.Single(entry => entry.Path == "dark/01.png").Split = "test";
        string manifest = files.PathFor("bad.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new SplitManifest { FormatVersion = 1, Entries = entries }, ModelFile.JsonOptions));
        Assert.Throws<InvalidDataException>(() => Dataset.Load(root, manifest, 42));
        File.Copy(Path.Combine(root, "dark", "00.png"), Path.Combine(root, "light", "00.png"), overwrite: true);
        Assert.Throws<InvalidDataException>(() => Dataset.Load(root, null, 42));
    }

    [Fact]
    public void InsufficientDataOrMissingManifestRowsFail()
    {
        using var files = new TestFiles();
        string root = files.Dataset(2);
        Assert.Throws<InvalidDataException>(() => Dataset.Load(root, null, 42));
        string manifest = files.PathFor("empty.json");
        File.WriteAllText(manifest, "{\"formatVersion\":1,\"entries\":[]}");
        Assert.Throws<InvalidDataException>(() => Dataset.Load(root, manifest, 42));
    }
}

using System.Text.Json;
using TrafficSign.Core;
using TrafficSign.Trainer;
using Xunit;

namespace TrafficSign.Tests;

public sealed class DatasetTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void UnassignedManifestMatchesDeterministicAutomaticSplit(int version)
    {
        using var files = new TestFiles();
        string root = files.Dataset();
        var automatic = Dataset.Load(root, null, 42);
        var entries = automatic.ToManifest().Entries;
        foreach (var entry in entries)
        {
            entry.Split = null;
        }
        string manifest = WriteManifest(files, version, entries);
        var first = Dataset.Load(root, manifest, 42);
        var repeated = Dataset.Load(root, manifest, 42);
        Assert.Equal(automatic.ToManifest().Entries.Select(entry => entry.Split), first.ToManifest().Entries.Select(entry => entry.Split));
        Assert.Equal(first.ToManifest().Entries.Select(entry => entry.Split), repeated.ToManifest().Entries.Select(entry => entry.Split));
        foreach (var split in Enum.GetValues<DatasetSplit>())
        {
            Assert.Equal(2, first.Samples.Where(sample => sample.Split == split).Select(sample => sample.LabelIndex).Distinct().Count());
        }
        Assert.Equal(first.Samples.Length, first.Samples.Select(sample => sample.RelativePath).Distinct().Count());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void OfficialTestIsPreservedAndValidationComesFromTrainingForEveryClass(int version)
    {
        using var files = new TestFiles();
        string root = files.Dataset();
        var entries = Dataset.Load(root, null, 42).ToManifest().Entries;
        foreach (var entry in entries)
        {
            entry.Split = entry.Path.EndsWith("00.png", StringComparison.Ordinal) ? "test" : "train";
        }
        string manifest = WriteManifest(files, version, entries);
        var dataset = Dataset.Load(root, manifest, 7);
        Assert.Equal(2, dataset.Samples.Count(sample => sample.Split == DatasetSplit.Test));
        Assert.All(dataset.Samples.Where(sample => sample.Split == DatasetSplit.Test), sample => Assert.EndsWith("00.png", sample.RelativePath));
        Assert.Equal(2, dataset.Samples.Where(sample => sample.Split == DatasetSplit.Validation).Select(sample => sample.LabelIndex).Distinct().Count());
        Assert.Equal(dataset.ToManifest().Entries.Select(entry => entry.Split), Dataset.Load(root, manifest, 7).ToManifest().Entries.Select(entry => entry.Split));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void FullManifestPreservesAssignmentsAndAcceptsCopiesAcrossSplitsAndLabels(int version, bool legacyMetadata)
    {
        using var files = new TestFiles();
        string root = files.Dataset(3);
        var dataset = Dataset.Load(root, null, 42);
        var entries = dataset.ToManifest().Entries;
        // Every image has identical content, including different labels and all three splits.
        foreach (var sample in dataset.Samples.Skip(1))
        {
            File.Copy(Path.Combine(root, dataset.Samples[0].RelativePath), Path.Combine(root, sample.RelativePath), overwrite: true);
        }
        string manifest = legacyMetadata
            ? WriteLegacyManifest(files, entries)
            : WriteManifest(files, version, entries);
        var output = new StringWriter();
        var loaded = Dataset.Load(root, manifest, 999, output);
        Assert.Equal(entries.Select(entry => entry.Split), loaded.ToManifest().Entries.Select(entry => entry.Split));
        Assert.Equal(legacyMetadata ? 1 : 0, output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Equal(2, loaded.ToManifest().FormatVersion);
        using var exported = JsonDocument.Parse(JsonSerializer.Serialize(loaded.ToManifest(), ModelFile.JsonOptions));
        Assert.All(exported.RootElement.GetProperty("entries").EnumerateArray(), entry =>
            Assert.Equal(new[] { "path", "split" }, entry.EnumerateObject().Select(property => property.Name)));
        Assert.Equal(6, Dataset.Load(root, null, 42).Samples.Length);
    }

    [Fact]
    public void LegacyUnassignedMetadataIsIgnoredAndChangedImagesAreAccepted()
    {
        using var files = new TestFiles();
        string root = files.Dataset();
        var automatic = Dataset.Load(root, null, 42);
        var entries = automatic.ToManifest().Entries;
        foreach (var entry in entries)
        {
            entry.Split = null;
        }
        string manifest = WriteLegacyManifest(files, entries);
        files.Image("data/dark/00.png", 100);
        var output = new StringWriter();
        var loaded = Dataset.Load(root, manifest, 42, output);
        Assert.Equal(automatic.ToManifest().Entries.Select(entry => entry.Split), loaded.ToManifest().Entries.Select(entry => entry.Split));
        Assert.Contains("ignored", output.ToString());
        Assert.NotEqual(automatic.Samples[0].Inputs, loaded.Samples[0].Inputs);
    }

    [Theory]
    [InlineData("../outside.png")]
    [InlineData("dark/../00.png")]
    [InlineData("C:/outside.png")]
    [InlineData("/outside.png")]
    [InlineData("")]
    public void UnsafeManifestPathIsRejected(string path)
    {
        using var files = new TestFiles();
        string root = files.Dataset(3);
        var entries = Dataset.Load(root, null, 42).ToManifest().Entries;
        entries[0] = new SplitEntry { Path = path, Split = "train" };
        Assert.Throws<InvalidDataException>(() => Dataset.Load(root, WriteManifest(files, 2, entries), 42));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("mixed")]
    [InlineData("unknown split")]
    [InlineData("missing class split")]
    [InlineData("too little train")]
    public void InvalidManifestStructureIsRejected(string problem)
    {
        using var files = new TestFiles();
        string root = files.Dataset(3);
        var entries = Dataset.Load(root, null, 42).ToManifest().Entries;
        switch (problem)
        {
            case "duplicate": entries[0] = entries[1]; break;
            case "missing": entries = entries[1..]; break;
            case "mixed": entries[0].Split = null; break;
            case "unknown split": entries[0].Split = "other"; break;
            case "missing class split":
                foreach (var entry in entries.Where(entry => entry.Path.StartsWith("dark/", StringComparison.Ordinal)))
                {
                    entry.Split = "train";
                }
                break;
            case "too little train":
                foreach (var entry in entries)
                {
                    entry.Split = entry.Path.EndsWith("00.png", StringComparison.Ordinal) ? "train" : "test";
                }
                break;
        }
        Assert.Throws<InvalidDataException>(() => Dataset.Load(root, WriteManifest(files, 2, entries), 42));
    }

    [Fact]
    public void AutomaticSplitRequiresThreeFilesPerClass()
    {
        using var files = new TestFiles();
        Assert.Contains("three files", Assert.Throws<InvalidDataException>(() => Dataset.Load(files.Dataset(2), null, 42)).Message);
    }

    private static string WriteManifest(TestFiles files, int version, SplitEntry[] entries)
    {
        string path = files.PathFor("manifest.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new SplitManifest { FormatVersion = version, Entries = entries }, ModelFile.JsonOptions));
        return path;
    }

    private static string WriteLegacyManifest(TestFiles files, SplitEntry[] entries)
    {
        string path = files.PathFor("legacy.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            FormatVersion = 1,
            Entries = entries.Select(entry => new { entry.Path, entry.Split, Sha256 = "obsolete hash", GroupId = "one former group across all labels and splits" })
        }, ModelFile.JsonOptions));
        return path;
    }
}

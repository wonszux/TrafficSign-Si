using System.Text.Json;
using TrafficSign.Core;
using TrafficSign.Trainer;
using Xunit;

namespace TrafficSign.Tests;

public sealed class DatasetContractTests
{
    [Fact]
    public void FileSplitMatchesThePreRefactoringSnapshot()
    {
        using var files = new TestFiles();
        string root = CreateSnapshotDataset(files);
        var dataset = Dataset.Load(root, null, 42);

        // Recorded from commit 618158c, with fixed encoded bytes rather than a platform encoder.
        Assert.Equal(new[] { "train", "validation", "test", "test", "train", "validation" },
            dataset.ToManifest().Entries.Select(entry => entry.Split));

        string manifest = files.PathFor("snapshot.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(dataset.ToManifest(), ModelFile.JsonOptions));
        Assert.Equal(dataset.ToManifest().Entries.Select(entry => entry.Split),
            Dataset.Load(root, manifest, 999).ToManifest().Entries.Select(entry => entry.Split));
    }

    [Theory]
    [InlineData("{\"entries\":[]}")]
    [InlineData("{\"formatVersion\":3,\"entries\":[]}")]
    [InlineData("{\"formatVersion\":\"1\",\"entries\":[]}")]
    [InlineData("{\"formatVersion\":1,\"entries\":null}")]
    [InlineData("{\"formatVersion\":1,\"entries\":[null]}")]
    public void InvalidManifestIsRejected(string json)
    {
        using var files = new TestFiles();
        string root = files.Dataset(3);
        string manifest = files.PathFor("invalid.json");
        File.WriteAllText(manifest, json);
        var error = new StringWriter();
        Assert.Equal(1, Program.Run([root, files.PathFor("model.json"), "--epochs", "1", "--hidden-size", "1", "--split-manifest", manifest], TextWriter.Null, error));
        Assert.Contains("manifest", error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(files.PathFor("model.json")));
    }

    [Fact]
    public void MissingManifestVersionIsRejectedEvenWhenAllImagesArePresent()
    {
        using var files = new TestFiles();
        string root = files.Dataset(3);
        var entries = Dataset.Load(root, null, 42).ToManifest().Entries;
        string manifest = files.PathFor("unversioned.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new { Entries = entries }, ModelFile.JsonOptions));
        Assert.Throws<JsonException>(() => Dataset.Load(root, manifest, 42));
    }

    private static string CreateSnapshotDataset(TestFiles files)
    {
        string[] encodedImages =
        [
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAABHNCSVQICAgIfAhkiAAAAA1JREFUCJljYGBg+A8AAQQBAH2yyN8AAAAASUVORK5CYII=",
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAABHNCSVQICAgIfAhkiAAAAA1JREFUCJlj0GBg+A8AAaQBKB+JtnwAAAAASUVORK5CYII=",
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAABHNCSVQICAgIfAhkiAAAAA1JREFUCJljCGBg+A8AAkQBULbG/4EAAAAASUVORK5CYII=",
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAABHNCSVQICAgIfAhkiAAAAA1JREFUCJljqGBg+A8AAuQBePNTAMoAAAAASUVORK5CYII=",
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAABHNCSVQICAgIfAhkiAAAAA1JREFUCJljWMDA8B8AA4QBoOWsU2wAAAAASUVORK5CYII=",
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAABHNCSVQICAgIfAhkiAAAAA1JREFUCJljOMHA8B8ABCQByCT2z+wAAAAASUVORK5CYII="
        ];
        for (int i = 0; i < encodedImages.Length; i++)
        {
            string path = files.PathFor($"snapshot/{(i < 3 ? "a" : "b")}/{i % 3}.png");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, Convert.FromBase64String(encodedImages[i]));
        }
        return files.PathFor("snapshot");
    }
}

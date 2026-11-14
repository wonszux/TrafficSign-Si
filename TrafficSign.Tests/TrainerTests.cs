using System.Globalization;
using System.Text.Json;
using TrafficSign.Core;
using TrafficSign.Trainer;
using Xunit;

namespace TrafficSign.Tests;

public sealed class TrainerTests
{
    [Fact]
    public void EvaluationUsesActualRowsPredictedColumnsAndZeroRecallForAbsentClasses()
    {
        var model = NeuralNetwork.Create(1, ["a", "b", "c"], 42).GetModel();
        Array.Clear(model.OutputWeights);
        model.OutputBiases[0] = Math.Log(0.6);
        model.OutputBiases[1] = Math.Log(0.3);
        model.OutputBiases[2] = Math.Log(0.1);
        var inputs = new double[ImagePreprocessor.InputSize];
        DatasetSample[] samples =
        [
            new("a/1.png", 0, DatasetSplit.Test, inputs),
            new("a/2.png", 0, DatasetSplit.Test, inputs),
            new("b/1.png", 1, DatasetSplit.Test, inputs)
        ];

        var metrics = Training.Evaluate(new NeuralNetwork(model), samples, 3);

        Assert.Equal(3, metrics.Count);
        Assert.Equal(2.0 / 3, metrics.Accuracy, 12);
        Assert.Equal((-2 * Math.Log(0.6) - Math.Log(0.3)) / 3, metrics.Loss, 12);
        Assert.Equal(new[] { 1.0, 0.0, 0.0 }, metrics.RecallPerClass);
        Assert.Equal(new[] { 2, 0, 0 }, metrics.ConfusionMatrix[0]);
        Assert.Equal(new[] { 1, 0, 0 }, metrics.ConfusionMatrix[1]);
        Assert.Equal(new[] { 0, 0, 0 }, metrics.ConfusionMatrix[2]);
    }

    [Fact]
    public void PublicationFailureRollsBackOnlyFilesCreatedByThisRun()
    {
        using var files = new TestFiles();
        string data = files.Dataset(3);
        string model = files.PathFor("model.json");
        string collision = files.PathFor("model.report.json");
        using var output = new OnEpochWriter(() => File.WriteAllText(collision, "preserve this file"));
        var error = new StringWriter();

        Assert.Equal(1, Program.Run([data, model, "--epochs", "1", "--hidden-size", "1"], output, error));
        Assert.False(File.Exists(model));
        Assert.False(File.Exists(files.PathFor("model.split.json")));
        Assert.Equal("preserve this file", File.ReadAllText(collision));
        Assert.Empty(Directory.GetFiles(files.Root, "*.tmp"));
        Assert.NotEmpty(error.ToString());
    }

    private sealed class OnEpochWriter(Action callback) : StringWriter
    {
        private bool _called;

        public override void WriteLine(string? value)
        {
            if (!_called && value?.StartsWith("Epoch ", StringComparison.Ordinal) == true)
            {
                _called = true;
                callback();
            }
            base.WriteLine(value);
        }
    }

    [Fact]
    public void SyntheticExperimentTrainsWritesArtifactsAndEvaluatesExactSavedModel()
    {
        using var files = new TestFiles();
        string data = files.Dataset(10);
        string modelPath = files.PathFor("result.json");
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var error = new StringWriter(CultureInfo.InvariantCulture);
        int result = Program.Run([data, modelPath, "--epochs", "30", "--hidden-size", "4", "--learning-rate", "0.03", "--seed", "42"], output, error);
        Assert.True(result == 0, error.ToString());
        var model = ModelFile.Load(modelPath);
        using var trainingReport = JsonDocument.Parse(File.ReadAllText(files.PathFor("result.report.json")));
        Assert.Equal(2, trainingReport.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.True(trainingReport.RootElement.GetProperty("selectedValidation").GetProperty("accuracy").GetDouble() >= 0.99);
        var report = trainingReport.RootElement;
        Assert.True(report.GetProperty("test").GetProperty("accuracy").GetDouble() >= 0.99);
        Assert.Equal(new[] { "formatVersion", "labels", "options", "bestEpoch", "classCounts", "selectedTrain", "selectedValidation", "test", "history" },
            report.EnumerateObject().Select(property => property.Name));
        var history = report.GetProperty("history").EnumerateArray().ToArray();
        var best = history.MinBy(epoch => epoch.GetProperty("validation").GetProperty("loss").GetDouble());
        Assert.Equal(best.GetProperty("epoch").GetInt32(), report.GetProperty("bestEpoch").GetInt32());
        Assert.All(history, epoch =>
        {
            foreach (string split in new[] { "train", "validation" })
            {
                Assert.Equal(new[] { "loss", "accuracy" }, epoch.GetProperty(split).EnumerateObject().Select(property => property.Name));
            }
        });
        var dataset = Dataset.Load(data, files.PathFor("result.split.json"), 0);
        foreach (var (split, name) in new[] { (DatasetSplit.Train, "selectedTrain"), (DatasetSplit.Validation, "selectedValidation"), (DatasetSplit.Test, "test") })
        {
            var metrics = Training.Evaluate(new NeuralNetwork(model), dataset.Samples.Where(sample => sample.Split == split).ToArray(), model.Labels.Length);
            var saved = report.GetProperty(name).Deserialize<Metrics>(ModelFile.JsonOptions)!;
            Assert.Equal(metrics.Count, saved.Count);
            Assert.Equal(metrics.Loss, saved.Loss, 12);
            Assert.Equal(metrics.Accuracy, saved.Accuracy, 12);
            Assert.Equal(metrics.RecallPerClass, saved.RecallPerClass);
            for (int i = 0; i < metrics.ConfusionMatrix.Length; i++)
            {
                Assert.Equal(metrics.ConfusionMatrix[i], saved.ConfusionMatrix[i]);
            }
        }
        Assert.Equal(3, Directory.GetFiles(files.Root, "*.json").Length);
        // No overwrite, and the report collision must be detected before a model is written.
        Assert.Equal(1, Program.Run([data, modelPath], output, error));
        string blockedModel = files.PathFor("blocked.json");
        File.WriteAllText(files.PathFor("blocked.report.json"), "keep me");
        Assert.Equal(1, Program.Run([data, blockedModel], output, error));
        Assert.False(File.Exists(blockedModel));
        Assert.Equal("keep me", File.ReadAllText(files.PathFor("blocked.report.json")));
    }

    [Fact]
    public void FinalMetricsUseBestEpochEvenWhenLaterEpochsAreWorse()
    {
        using var files = new TestFiles();
        string data = files.Dataset();
        var dataset = Dataset.Load(data, null, 42);
        string manifest = files.PathFor("input.split.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(dataset.ToManifest(), ModelFile.JsonOptions));
        // Reverse validation patterns so further fitting of train eventually worsens validation loss.
        foreach (var sample in dataset.Samples.Where(sample => sample.Split == DatasetSplit.Validation))
        {
            files.Image("data/" + sample.RelativePath, sample.LabelIndex == 0 ? 240 : 5, vertical: sample.LabelIndex == 0);
        }
        string modelPath = files.PathFor("best.json");
        Assert.Equal(0, Program.Run([data, modelPath, "--epochs", "20", "--hidden-size", "4", "--learning-rate", "0.03", "--split-manifest", manifest], TextWriter.Null, TextWriter.Null));
        using var report = JsonDocument.Parse(File.ReadAllText(files.PathFor("best.report.json")));
        var root = report.RootElement;
        Assert.True(root.GetProperty("bestEpoch").GetInt32() < 20);
        double bestLoss = root.GetProperty("history").EnumerateArray().Min(epoch => epoch.GetProperty("validation").GetProperty("loss").GetDouble());
        Assert.Equal(bestLoss, root.GetProperty("selectedValidation").GetProperty("loss").GetDouble(), 12);
        var selectedNetwork = new NeuralNetwork(ModelFile.Load(modelPath));
        var loaded = Dataset.Load(data, manifest, 0);
        foreach (var (split, name) in new[] { (DatasetSplit.Validation, "selectedValidation"), (DatasetSplit.Test, "test") })
        {
            var metrics = Training.Evaluate(selectedNetwork, loaded.Samples.Where(sample => sample.Split == split).ToArray(), 2);
            Assert.Equal(root.GetProperty(name).GetProperty("loss").GetDouble(), metrics.Loss, 12);
        }
    }

    [Fact]
    public void TestImagesDoNotAffectHistoryOrSelectedModel()
    {
        using var files = new TestFiles();
        string data = files.Dataset();
        string first = files.PathFor("first.json");
        string second = files.PathFor("second.json");
        string[] options = ["--epochs", "20", "--hidden-size", "4", "--learning-rate", "0.5", "--seed", "42"];
        Assert.Equal(0, Program.Run([data, first, .. options], TextWriter.Null, TextWriter.Null));
        var dataset = Dataset.Load(data, files.PathFor("first.split.json"), 0);
        foreach (var sample in dataset.Samples.Where(sample => sample.Split == DatasetSplit.Test))
        {
            files.Image("data/" + sample.RelativePath, sample.LabelIndex == 0 ? 240 : 5, vertical: sample.LabelIndex == 0);
        }
        Assert.Equal(0, Program.Run([data, second, .. options, "--split-manifest", files.PathFor("first.split.json")], TextWriter.Null, TextWriter.Null));
        Assert.Equal(File.ReadAllText(first), File.ReadAllText(second));
        using var firstReport = JsonDocument.Parse(File.ReadAllText(files.PathFor("first.report.json")));
        using var secondReport = JsonDocument.Parse(File.ReadAllText(files.PathFor("second.report.json")));
        Assert.Equal(firstReport.RootElement.GetProperty("history").GetRawText(), secondReport.RootElement.GetProperty("history").GetRawText());
        Assert.NotEqual(firstReport.RootElement.GetProperty("test").GetRawText(), secondReport.RootElement.GetProperty("test").GetRawText());
    }

    [Fact]
    public void RemovedEvaluateCommandIsNotAdvertisedOrAccepted()
    {
        var help = new StringWriter();
        Assert.Equal(0, Program.Run(["--help"], help, TextWriter.Null));
        Assert.DoesNotContain("evaluate", help.ToString());
        Assert.Equal(1, Program.Run(["evaluate", "model.json", "data", "split.json"], TextWriter.Null, TextWriter.Null));
    }

    [Theory]
    [InlineData("--unknown", "42")]
    [InlineData("--epochs", "0")]
    [InlineData("--epochs", "10001")]
    [InlineData("--learning-rate", "NaN")]
    [InlineData("--learning-rate", "0,1")]
    [InlineData("--learning-rate", "0")]
    [InlineData("--learning-rate", "11")]
    [InlineData("--hidden-size", "0")]
    [InlineData("--hidden-size", "2049")]
    public void CliRejectsInvalidOptions(string option, string value)
    {
        using var files = new TestFiles();
        string data = files.Dataset(3);
        var options = new Dictionary<string, string>
        {
            ["--epochs"] = "1",
            ["--hidden-size"] = "1",
            ["--learning-rate"] = "0.01"
        };
        options[option] = value;
        string[] args = [data, files.PathFor("output.json"), .. options.SelectMany(pair => new[] { pair.Key, pair.Value })];
        var error = new StringWriter();
        Assert.Equal(1, Program.Run(args, TextWriter.Null, error));
        Assert.Contains(option.TrimStart('-'), error.ToString());
        Assert.Empty(Directory.GetFiles(files.Root, "*.json"));
    }

    [Fact]
    public void CliReportsDamagedDataAndNeverPublishesModel()
    {
        using var files = new TestFiles();
        string root = files.Dataset();
        File.WriteAllText(Path.Combine(root, "dark", "00.png"), "broken");
        var error = new StringWriter();
        Assert.Equal(1, Program.Run([root, files.PathFor("model.json"), "--epochs", "1"], TextWriter.Null, error));
        Assert.Contains("00.png", error.ToString());
        Assert.False(File.Exists(files.PathFor("model.json")));
    }

    [Fact]
    public void HelpAndMissingOrDuplicateValuesHaveDefinedExitCodes()
    {
        using var files = new TestFiles();
        string data = files.Dataset(3);
        string model = files.PathFor("output.json");
        Assert.Equal(0, Program.Run(["--help"], TextWriter.Null, TextWriter.Null));
        Assert.Equal(1, Program.Run([], TextWriter.Null, TextWriter.Null));
        var missing = new StringWriter();
        Assert.Equal(1, Program.Run([data, model, "--epochs"], TextWriter.Null, missing));
        Assert.Contains("Missing value for --epochs", missing.ToString());
        var duplicate = new StringWriter();
        Assert.Equal(1, Program.Run([data, model, "--epochs", "1", "--epochs", "2"], TextWriter.Null, duplicate));
        Assert.Contains("Duplicate option: --epochs", duplicate.ToString());
        Assert.Empty(Directory.GetFiles(files.Root, "*.json"));
    }
}

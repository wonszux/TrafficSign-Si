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
            new("a/1.png", 0, "", null, DatasetSplit.Test, inputs),
            new("a/2.png", 0, "", null, DatasetSplit.Test, inputs),
            new("b/1.png", 1, "", null, DatasetSplit.Test, inputs)
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
        Assert.False(trainingReport.RootElement.GetProperty("testEvaluated").GetBoolean());
        Assert.False(trainingReport.RootElement.GetProperty("completeGroupingMetadata").GetBoolean());
        Assert.True(trainingReport.RootElement.GetProperty("selectedValidation").GetProperty("accuracy").GetDouble() >= 0.99);
        Assert.Equal(0, Program.Run(["evaluate", modelPath, data, files.PathFor("result.split.json")], output, error));
        using var testReport = JsonDocument.Parse(File.ReadAllText(files.PathFor("result.test-report.json")));
        Assert.True(testReport.RootElement.GetProperty("testEvaluated").GetBoolean());
        Assert.True(testReport.RootElement.GetProperty("test").GetProperty("accuracy").GetDouble() >= 0.99);
        // No overwrite, and the report collision must be detected before a model is written.
        Assert.Equal(1, Program.Run([data, modelPath], output, error));
        string blockedModel = files.PathFor("blocked.json");
        File.WriteAllText(files.PathFor("blocked.report.json"), "keep me");
        Assert.Equal(1, Program.Run([data, blockedModel], output, error));
        Assert.False(File.Exists(blockedModel));
        Assert.Equal("keep me", File.ReadAllText(files.PathFor("blocked.report.json")));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"formatVersion\":\"1\"}")]
    [InlineData("{\"formatVersion\":1,\"modelSha256\":8}")]
    public void SavedEvaluationRejectsMalformedTrainingReportWithoutCrashing(string report)
    {
        using var files = new TestFiles();
        string data = files.Dataset();
        string model = files.PathFor("model.json");
        Assert.Equal(0, Program.Run([data, model, "--epochs", "1", "--hidden-size", "2"], TextWriter.Null, TextWriter.Null));
        File.WriteAllText(files.PathFor("model.report.json"), report);
        var error = new StringWriter();
        Assert.Equal(1, Program.Run(["evaluate", model, data, files.PathFor("model.split.json")], TextWriter.Null, error));
        Assert.NotEmpty(error.ToString());
        Assert.False(File.Exists(files.PathFor("model.test-report.json")));
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

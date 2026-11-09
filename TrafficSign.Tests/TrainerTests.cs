using System.Globalization;
using System.Text.Json;
using TrafficSign.Core;
using TrafficSign.Trainer;
using Xunit;

namespace TrafficSign.Tests;

public sealed class TrainerTests
{
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
    [InlineData("--learning-rate", "NaN")]
    [InlineData("--learning-rate", "0,1")]
    [InlineData("--hidden-size", "0")]
    public void CliRejectsInvalidOptions(string option, string value)
    {
        var error = new StringWriter();
        Assert.Equal(1, Program.Run(["missing-data", "output.json", option, value], TextWriter.Null, error));
        Assert.NotEmpty(error.ToString());
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
        Assert.Equal(0, Program.Run(["--help"], TextWriter.Null, TextWriter.Null));
        Assert.Equal(1, Program.Run([], TextWriter.Null, TextWriter.Null));
        Assert.Equal(1, Program.Run(["a", "b", "--epochs"], TextWriter.Null, TextWriter.Null));
        Assert.Equal(1, Program.Run(["a", "b", "--epochs", "1", "--epochs", "2"], TextWriter.Null, TextWriter.Null));
    }
}

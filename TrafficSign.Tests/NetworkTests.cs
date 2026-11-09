using TrafficSign.Core;
using Xunit;

namespace TrafficSign.Tests;

public sealed class NetworkTests
{
    [Fact]
    public void ForwardMatchesKnownWeightsAndOwnsParameters()
    {
        var model = NeuralNetwork.Create(1, ["a", "b"], 42).GetModel();
        Array.Clear(model.HiddenWeights);
        model.HiddenWeights[0] = 0.5;
        model.HiddenBiases[0] = 0.1;
        model.OutputWeights[0] = 0.7;
        model.OutputWeights[1] = -0.4;
        model.OutputBiases[0] = 0.2;
        model.OutputBiases[1] = -0.3;
        var network = new NeuralNetwork(model);
        model.HiddenWeights[0] = 100; // Must not mutate the constructed network.
        var inputs = new double[ImagePreprocessor.InputSize];
        inputs[0] = 0.8;
        double h = Math.Tanh(0.5);
        double a = 0.7 * h + 0.2, b = -0.4 * h - 0.3;
        double expected = 1 / (1 + Math.Exp(b - a));
        Assert.Equal(expected, network.Predict(inputs)[0], 12);
        Assert.Equal(1.0, network.Predict(inputs).Sum(), 12);
        var exported = network.GetModel();
        exported.OutputBiases[0] = 500;
        Assert.Equal(expected, network.Predict(inputs)[0], 12);
    }

    [Fact]
    public void LossStaysAccurateForHugeEqualLogits()
    {
        var model = NeuralNetwork.Create(1, ["a", "b"], 42).GetModel();
        Array.Clear(model.OutputWeights);
        model.OutputBiases[0] = 1e16;
        model.OutputBiases[1] = 1e16;
        var network = new NeuralNetwork(model);
        Assert.Equal(Math.Log(2), network.Loss(new double[ImagePreprocessor.InputSize], 0), 12);
        Assert.Equal(0.5, network.Predict(new double[ImagePreprocessor.InputSize])[0], 12);
    }

    [Fact]
    public void BackpropMatchesNumericalGradientForEveryParameter()
    {
        var model = NeuralNetwork.Create(2, ["a", "b"], 5).GetModel();
        var input = Enumerable.Range(0, ImagePreprocessor.InputSize).Select(i => Math.Sin(i) * 0.02).ToArray();
        var gradient = new NeuralNetwork(model).ComputeGradients(input, 1);
        Check(model.HiddenWeights, gradient.HiddenWeights);
        Check(model.HiddenBiases, gradient.HiddenBiases);
        Check(model.OutputWeights, gradient.OutputWeights);
        Check(model.OutputBiases, gradient.OutputBiases);

        void Check(double[] parameters, double[] expected)
        {
            const double epsilon = 1e-5;
            for (int i = 0; i < parameters.Length; i++)
            {
                double original = parameters[i];
                parameters[i] = original + epsilon;
                double plus = new NeuralNetwork(model).Loss(input, 1);
                parameters[i] = original - epsilon;
                double minus = new NeuralNetwork(model).Loss(input, 1);
                parameters[i] = original;
                double numerical = (plus - minus) / (2 * epsilon);
                Assert.True(Math.Abs(numerical - expected[i]) <= 1e-7 + 1e-4 * Math.Max(Math.Abs(numerical), Math.Abs(expected[i])),
                    $"Gradient {i}: numerical={numerical}, backprop={expected[i]}");
            }
        }
    }

    [Fact]
    public void TinyTrainingMemorizesOppositeExamples()
    {
        var network = NeuralNetwork.Create(4, ["a", "b"], 123);
        var a = new double[ImagePreprocessor.InputSize];
        var b = new double[ImagePreprocessor.InputSize];
        a[0] = 1; b[1] = 1;
        double before = network.Loss(a, 0) + network.Loss(b, 1);
        for (int i = 0; i < 300; i++) { network.TrainSample(a, 0, 0.1); network.TrainSample(b, 1, 0.1); }
        Assert.True(network.Loss(a, 0) + network.Loss(b, 1) < before * 0.1);
        Assert.True(network.Predict(a)[0] > 0.95);
        Assert.True(network.Predict(b)[1] > 0.95);
    }

    [Fact]
    public void ModelRoundTripAndInvalidContracts()
    {
        using var files = new TestFiles();
        var model = NeuralNetwork.Create(3, ["a", "b"], 42).GetModel();
        string path = files.PathFor("model.json");
        ModelFile.Save(path, model);
        var loaded = ModelFile.Load(path);
        var input = Enumerable.Repeat(0.5, ImagePreprocessor.InputSize).ToArray();
        Assert.Equal(new NeuralNetwork(model).Predict(input), new NeuralNetwork(loaded).Predict(input));
        Assert.Throws<IOException>(() => ModelFile.Save(path, model));
        model.HiddenWeights[0] = double.NaN;
        Assert.Throws<InvalidDataException>(model.Validate);
        var invalid = NeuralNetwork.Create(1, ["a", "b"], 42).GetModel();
        File.WriteAllText(path, "[]");
        Assert.Throws<System.Text.Json.JsonException>(() => ModelFile.Load(path));
        File.WriteAllText(path, "{\"formatVersion\":1}");
        Assert.Throws<System.Text.Json.JsonException>(() => ModelFile.Load(path));
    }

    [Fact]
    public void ModelRejectsMismatchedPreprocessingAndDimensions()
    {
        var model = NeuralNetwork.Create(1, ["a", "b"], 42).GetModel();
        var invalid = new ModelData
        {
            FormatVersion = 1, HiddenSize = model.HiddenSize, Labels = model.Labels,
            Preprocessing = new() { Id = "gray32-v1", Width = 64, Height = 32, Channels = 1 },
            HiddenWeights = model.HiddenWeights, HiddenBiases = model.HiddenBiases,
            OutputWeights = model.OutputWeights, OutputBiases = model.OutputBiases
        };
        Assert.Throws<InvalidDataException>(invalid.Validate);
        Assert.Throws<ArgumentException>(() => new NeuralNetwork(model).Predict([1.0]));
    }
}

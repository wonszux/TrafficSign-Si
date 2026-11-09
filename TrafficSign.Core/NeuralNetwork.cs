namespace TrafficSign.Core;

public sealed record NetworkGradients(double[] HiddenWeights, double[] HiddenBiases,
    double[] OutputWeights, double[] OutputBiases);

public sealed class NeuralNetwork
{
    private readonly string[] labels;
    private readonly int hiddenSize;
    private readonly double[] hiddenWeights, hiddenBiases, outputWeights, outputBiases;

    public NeuralNetwork(ModelData model)
    {
        ArgumentNullException.ThrowIfNull(model);
        model.Validate();
        labels = (string[])model.Labels.Clone();
        hiddenSize = model.HiddenSize;
        hiddenWeights = (double[])model.HiddenWeights.Clone();
        hiddenBiases = (double[])model.HiddenBiases.Clone();
        outputWeights = (double[])model.OutputWeights.Clone();
        outputBiases = (double[])model.OutputBiases.Clone();
    }

    public static NeuralNetwork Create(int hiddenSize, string[] labels, int seed)
    {
        if (hiddenSize is < 1 or > 2048) throw new ArgumentOutOfRangeException(nameof(hiddenSize));
        ArgumentNullException.ThrowIfNull(labels);
        var random = new Random(seed);
        double[] Weights(int inputs, int outputs)
        {
            double limit = Math.Sqrt(6.0 / (inputs + outputs));
            return Enumerable.Range(0, checked(inputs * outputs))
                .Select(_ => (random.NextDouble() * 2 - 1) * limit).ToArray();
        }
        return new NeuralNetwork(new ModelData
        {
            FormatVersion = 1, Preprocessing = PreprocessingData.Gray32(), HiddenSize = hiddenSize,
            Labels = (string[])labels.Clone(), HiddenWeights = Weights(ImagePreprocessor.InputSize, hiddenSize),
            HiddenBiases = new double[hiddenSize], OutputWeights = Weights(hiddenSize, labels.Length),
            OutputBiases = new double[labels.Length]
        });
    }

    public ModelData GetModel() => new()
    {
        FormatVersion = 1, Preprocessing = PreprocessingData.Gray32(), HiddenSize = hiddenSize,
        Labels = (string[])labels.Clone(), HiddenWeights = (double[])hiddenWeights.Clone(),
        HiddenBiases = (double[])hiddenBiases.Clone(), OutputWeights = (double[])outputWeights.Clone(),
        OutputBiases = (double[])outputBiases.Clone()
    };

    public double[] Predict(double[] inputs) => Forward(inputs).Probabilities;

    public double Loss(double[] inputs, int target)
    {
        ValidateTarget(target);
        var state = Forward(inputs);
        return state.LogSum - (state.Logits[target] - state.Maximum);
    }

    public NetworkGradients ComputeGradients(double[] inputs, int target)
    {
        ValidateTarget(target);
        var state = Forward(inputs);
        double[] delta = state.Probabilities;
        delta[target] -= 1;
        var outputGradient = new double[outputWeights.Length];
        var hiddenDelta = new double[hiddenSize];
        for (int k = 0; k < labels.Length; k++)
            for (int h = 0; h < hiddenSize; h++)
            {
                outputGradient[k * hiddenSize + h] = delta[k] * state.Hidden[h];
                hiddenDelta[h] += delta[k] * outputWeights[k * hiddenSize + h];
            }
        var hiddenGradient = new double[hiddenWeights.Length];
        for (int h = 0; h < hiddenSize; h++)
        {
            hiddenDelta[h] *= 1 - state.Hidden[h] * state.Hidden[h];
            for (int d = 0; d < inputs.Length; d++)
                hiddenGradient[h * inputs.Length + d] = hiddenDelta[h] * inputs[d];
        }
        return new NetworkGradients(hiddenGradient, hiddenDelta, outputGradient, delta);
    }

    public void TrainSample(double[] inputs, int target, double learningRate)
    {
        if (!double.IsFinite(learningRate) || learningRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(learningRate));
        var gradient = ComputeGradients(inputs, target);
        Update(hiddenWeights, gradient.HiddenWeights);
        Update(hiddenBiases, gradient.HiddenBiases);
        Update(outputWeights, gradient.OutputWeights);
        Update(outputBiases, gradient.OutputBiases);
        void Update(double[] parameters, double[] values)
        {
            for (int i = 0; i < parameters.Length; i++)
            {
                parameters[i] -= learningRate * values[i];
                if (!double.IsFinite(parameters[i]))
                    throw new ArithmeticException("Training produced a non-finite parameter. Reduce the learning rate.");
            }
        }
    }

    private (double[] Hidden, double[] Logits, double[] Probabilities, double Maximum, double LogSum) Forward(double[] inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Length != ImagePreprocessor.InputSize || inputs.Any(x => !double.IsFinite(x)))
            throw new ArgumentException($"Expected {ImagePreprocessor.InputSize} finite input values.", nameof(inputs));
        var hidden = new double[hiddenSize];
        for (int h = 0; h < hiddenSize; h++)
        {
            double sum = hiddenBiases[h];
            for (int d = 0; d < inputs.Length; d++) sum += hiddenWeights[h * inputs.Length + d] * inputs[d];
            hidden[h] = Math.Tanh(sum);
        }
        var logits = new double[labels.Length];
        for (int k = 0; k < labels.Length; k++)
        {
            double sum = outputBiases[k];
            for (int h = 0; h < hiddenSize; h++) sum += outputWeights[k * hiddenSize + h] * hidden[h];
            logits[k] = sum;
        }
        double maximum = logits.Max();
        var probabilities = logits.Select(x => Math.Exp(x - maximum)).ToArray();
        double total = probabilities.Sum();
        double logSum = Math.Log(total);
        if (!double.IsFinite(maximum) || !double.IsFinite(logSum)) throw new ArithmeticException("Network produced non-finite logits.");
        for (int k = 0; k < probabilities.Length; k++) probabilities[k] /= total;
        return (hidden, logits, probabilities, maximum, logSum);
    }

    private void ValidateTarget(int target)
    {
        if ((uint)target >= labels.Length) throw new ArgumentOutOfRangeException(nameof(target));
    }
}

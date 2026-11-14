namespace TrafficSign.Core;

/// <summary>Gradients of the cross-entropy loss, with the same flattened layout as <see cref="ModelData"/>.</summary>
public sealed record NetworkGradients(double[] HiddenWeights, double[] HiddenBiases,
    double[] OutputWeights, double[] OutputBiases);

/// <summary>The predicted class index and cross-entropy loss from a single forward pass.</summary>
public readonly record struct SampleEvaluation(int PredictedIndex, double Loss);

/// <summary>A single-hidden-layer tanh classifier with a softmax output.</summary>
/// <remarks>Training mutates the network. Do not train concurrently with prediction or other training calls.</remarks>
public sealed class NeuralNetwork
{
    private readonly record struct ForwardState(
        double[] Hidden, double[] Logits, double[] Probabilities, double MaximumLogit, double LogExponentialSum);

    private readonly string[] _labels;
    private readonly int _hiddenSize;
    private readonly double[] _hiddenWeights;
    private readonly double[] _hiddenBiases;
    private readonly double[] _outputWeights;
    private readonly double[] _outputBiases;

    /// <summary>Validates a model and takes an independent copy of its labels and parameters.</summary>
    public NeuralNetwork(ModelData model)
    {
        ArgumentNullException.ThrowIfNull(model);
        model.Validate();
        _labels = (string[])model.Labels.Clone();
        _hiddenSize = model.HiddenSize;
        _hiddenWeights = (double[])model.HiddenWeights.Clone();
        _hiddenBiases = (double[])model.HiddenBiases.Clone();
        _outputWeights = (double[])model.OutputWeights.Clone();
        _outputBiases = (double[])model.OutputBiases.Clone();
    }

    /// <summary>Creates a network using seeded Xavier uniform initialization and zero biases.</summary>
    public static NeuralNetwork Create(int hiddenSize, string[] labels, int seed)
    {
        if (hiddenSize is < 1 or > 2048)
        {
            throw new ArgumentOutOfRangeException(nameof(hiddenSize));
        }
        ArgumentNullException.ThrowIfNull(labels);
        var random = new Random(seed);
        double[] CreateWeights(int inputCount, int outputCount)
        {
            double limit = Math.Sqrt(6.0 / (inputCount + outputCount));
            return Enumerable.Range(0, checked(inputCount * outputCount))
                .Select(_ => (random.NextDouble() * 2 - 1) * limit).ToArray();
        }
        return new NeuralNetwork(new ModelData
        {
            FormatVersion = ModelData.CurrentFormatVersion,
            Preprocessing = PreprocessingData.Gray32(),
            HiddenSize = hiddenSize,
            Labels = (string[])labels.Clone(),
            HiddenWeights = CreateWeights(ImagePreprocessor.InputSize, hiddenSize),
            HiddenBiases = new double[hiddenSize],
            OutputWeights = CreateWeights(hiddenSize, labels.Length),
            OutputBiases = new double[labels.Length]
        });
    }

    /// <summary>Returns an independent snapshot suitable for serialization or later restoration.</summary>
    public ModelData GetModel() => new()
    {
        FormatVersion = ModelData.CurrentFormatVersion,
        Preprocessing = PreprocessingData.Gray32(),
        HiddenSize = _hiddenSize,
        Labels = (string[])_labels.Clone(),
        HiddenWeights = (double[])_hiddenWeights.Clone(),
        HiddenBiases = (double[])_hiddenBiases.Clone(),
        OutputWeights = (double[])_outputWeights.Clone(),
        OutputBiases = (double[])_outputBiases.Clone()
    };

    /// <summary>Returns softmax scores in model label order.</summary>
    /// <param name="inputs">Exactly <see cref="ImagePreprocessor.InputSize"/> finite values.</param>
    public double[] Predict(double[] inputs) => Forward(inputs).Probabilities;

    /// <summary>Computes numerically stable cross-entropy for the zero-based target class.</summary>
    public double Loss(double[] inputs, int target)
    {
        ValidateTarget(target);
        return CrossEntropy(Forward(inputs), target);
    }

    /// <summary>Evaluates prediction and loss together, resolving tied scores to the first model label.</summary>
    public SampleEvaluation EvaluateSample(double[] inputs, int target)
    {
        ValidateTarget(target);
        var state = Forward(inputs);
        int predictedIndex = Array.IndexOf(state.Probabilities, state.Probabilities.Max());
        return new SampleEvaluation(predictedIndex, CrossEntropy(state, target));
    }

    /// <summary>Computes gradients for one example without changing the network or its inputs.</summary>
    public NetworkGradients ComputeGradients(double[] inputs, int target)
    {
        ValidateTarget(target);
        var state = Forward(inputs);
        double[] outputDeltas = state.Probabilities;
        outputDeltas[target] -= 1;
        var outputWeightGradients = new double[_outputWeights.Length];
        var hiddenDeltas = new double[_hiddenSize];
        for (int classIndex = 0; classIndex < _labels.Length; classIndex++)
        {
            for (int hiddenIndex = 0; hiddenIndex < _hiddenSize; hiddenIndex++)
            {
                int weightIndex = classIndex * _hiddenSize + hiddenIndex;
                outputWeightGradients[weightIndex] = outputDeltas[classIndex] * state.Hidden[hiddenIndex];
                hiddenDeltas[hiddenIndex] += outputDeltas[classIndex] * _outputWeights[weightIndex];
            }
        }
        var hiddenWeightGradients = new double[_hiddenWeights.Length];
        for (int hiddenIndex = 0; hiddenIndex < _hiddenSize; hiddenIndex++)
        {
            hiddenDeltas[hiddenIndex] *= 1 - state.Hidden[hiddenIndex] * state.Hidden[hiddenIndex];
            for (int inputIndex = 0; inputIndex < inputs.Length; inputIndex++)
            {
                hiddenWeightGradients[hiddenIndex * inputs.Length + inputIndex] = hiddenDeltas[hiddenIndex] * inputs[inputIndex];
            }
        }
        return new NetworkGradients(hiddenWeightGradients, hiddenDeltas, outputWeightGradients, outputDeltas);
    }

    /// <summary>Applies one stochastic gradient descent update in place.</summary>
    /// <exception cref="ArithmeticException">An update overflowed. The network may be partially updated and should be discarded.</exception>
    public void TrainSample(double[] inputs, int target, double learningRate)
    {
        if (!double.IsFinite(learningRate) || learningRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(learningRate));
        }
        var gradient = ComputeGradients(inputs, target);
        Update(_hiddenWeights, gradient.HiddenWeights);
        Update(_hiddenBiases, gradient.HiddenBiases);
        Update(_outputWeights, gradient.OutputWeights);
        Update(_outputBiases, gradient.OutputBiases);

        void Update(double[] parameters, double[] gradients)
        {
            for (int i = 0; i < parameters.Length; i++)
            {
                parameters[i] -= learningRate * gradients[i];
                if (!double.IsFinite(parameters[i]))
                {
                    throw new ArithmeticException("Training produced a non-finite parameter. Reduce the learning rate.");
                }
            }
        }
    }

    private ForwardState Forward(double[] inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Length != ImagePreprocessor.InputSize || inputs.Any(value => !double.IsFinite(value)))
        {
            throw new ArgumentException($"Expected {ImagePreprocessor.InputSize} finite input values.", nameof(inputs));
        }
        double[] hidden = ComputeHiddenActivations(inputs);
        double[] logits = ComputeLogits(hidden);
        // Subtracting the maximum prevents overflow without changing the softmax probabilities.
        double maximumLogit = logits.Max();
        var probabilities = logits.Select(logit => Math.Exp(logit - maximumLogit)).ToArray();
        double exponentialSum = probabilities.Sum();
        double logExponentialSum = Math.Log(exponentialSum);
        if (!double.IsFinite(maximumLogit) || !double.IsFinite(logExponentialSum))
        {
            throw new ArithmeticException("Network produced non-finite logits.");
        }
        for (int classIndex = 0; classIndex < probabilities.Length; classIndex++)
        {
            probabilities[classIndex] /= exponentialSum;
        }
        return new ForwardState(hidden, logits, probabilities, maximumLogit, logExponentialSum);
    }

    private double[] ComputeHiddenActivations(double[] inputs)
    {
        var hidden = new double[_hiddenSize];
        for (int hiddenIndex = 0; hiddenIndex < _hiddenSize; hiddenIndex++)
        {
            double sum = _hiddenBiases[hiddenIndex];
            for (int inputIndex = 0; inputIndex < inputs.Length; inputIndex++)
            {
                sum += _hiddenWeights[hiddenIndex * inputs.Length + inputIndex] * inputs[inputIndex];
            }
            hidden[hiddenIndex] = Math.Tanh(sum);
        }
        return hidden;
    }

    private double[] ComputeLogits(double[] hidden)
    {
        var logits = new double[_labels.Length];
        for (int classIndex = 0; classIndex < _labels.Length; classIndex++)
        {
            double sum = _outputBiases[classIndex];
            for (int hiddenIndex = 0; hiddenIndex < _hiddenSize; hiddenIndex++)
            {
                sum += _outputWeights[classIndex * _hiddenSize + hiddenIndex] * hidden[hiddenIndex];
            }
            logits[classIndex] = sum;
        }
        return logits;
    }

    private void ValidateTarget(int target)
    {
        if ((uint)target >= _labels.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(target));
        }
    }

    // Keep the subtraction grouped to retain precision when logits are large and nearly equal.
    private static double CrossEntropy(ForwardState state, int target) =>
        state.LogExponentialSum - (state.Logits[target] - state.MaximumLogit);
}

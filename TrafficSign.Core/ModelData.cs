namespace TrafficSign.Core;

public sealed class PreprocessingData
{
    public required string Id { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int Channels { get; init; }

    public static PreprocessingData Gray32() => new() { Id = "gray32-v1", Width = 32, Height = 32, Channels = 1 };
}

// A serialization DTO. NeuralNetwork takes an independent copy of all parameters.
public sealed class ModelData
{
    public required int FormatVersion { get; init; }
    public required PreprocessingData Preprocessing { get; init; }
    public required int HiddenSize { get; init; }
    public required string[] Labels { get; init; }
    public required double[] HiddenWeights { get; init; }
    public required double[] HiddenBiases { get; init; }
    public required double[] OutputWeights { get; init; }
    public required double[] OutputBiases { get; init; }

    public void Validate()
    {
        if (FormatVersion != 1) throw new InvalidDataException("Unsupported model formatVersion. Expected 1.");
        if (Preprocessing is null || Preprocessing.Id != "gray32-v1" ||
            Preprocessing.Width != 32 || Preprocessing.Height != 32 || Preprocessing.Channels != 1)
            throw new InvalidDataException("Unsupported preprocessing. Expected gray32-v1, 32x32, one channel.");
        if (HiddenSize is < 1 or > 2048) throw new InvalidDataException("hiddenSize must be between 1 and 2048.");
        if (Labels is null || Labels.Length is < 2 or > 1000 || Labels.Any(string.IsNullOrWhiteSpace) ||
            Labels.Distinct(StringComparer.Ordinal).Count() != Labels.Length)
            throw new InvalidDataException("Expected 2–1000 distinct, nonempty class labels.");
        ValidateArray(HiddenWeights, checked(HiddenSize * ImagePreprocessor.InputSize), "hiddenWeights");
        ValidateArray(HiddenBiases, HiddenSize, "hiddenBiases");
        ValidateArray(OutputWeights, checked(Labels.Length * HiddenSize), "outputWeights");
        ValidateArray(OutputBiases, Labels.Length, "outputBiases");
    }

    private static void ValidateArray(double[]? values, int length, string name)
    {
        if (values is null || values.Length != length || values.Any(value => !double.IsFinite(value)))
            throw new InvalidDataException($"Invalid {name}: expected {length} finite numbers.");
    }
}

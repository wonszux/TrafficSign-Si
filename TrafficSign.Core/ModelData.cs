namespace TrafficSign.Core;

/// <summary>Describes the input transformation required by a serialized model.</summary>
public sealed class PreprocessingData
{
    internal const string Gray32Id = "gray32-v1";

    public required string Id { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int Channels { get; init; }

    public static PreprocessingData Gray32() => new()
    {
        Id = Gray32Id,
        Width = ImagePreprocessor.Width,
        Height = ImagePreprocessor.Height,
        Channels = 1
    };
}

/// <summary>The versioned JSON model contract, including label order and network parameters.</summary>
/// <remarks>Weight arrays are row-major: hidden neuron × input, then output class × hidden neuron.</remarks>
public sealed class ModelData
{
    internal const int CurrentFormatVersion = 1;

    public required int FormatVersion { get; init; }
    public required PreprocessingData Preprocessing { get; init; }
    public required int HiddenSize { get; init; }
    public required string[] Labels { get; init; }
    public required double[] HiddenWeights { get; init; }
    public required double[] HiddenBiases { get; init; }
    public required double[] OutputWeights { get; init; }
    public required double[] OutputBiases { get; init; }

    /// <summary>Checks format compatibility, label uniqueness, parameter dimensions and finite values.</summary>
    /// <exception cref="InvalidDataException">The model is unsupported or inconsistent.</exception>
    public void Validate()
    {
        if (FormatVersion != CurrentFormatVersion)
        {
            throw new InvalidDataException("Unsupported model formatVersion. Expected 1.");
        }

        if (Preprocessing is null || Preprocessing.Id != PreprocessingData.Gray32Id ||
            Preprocessing.Width != ImagePreprocessor.Width || Preprocessing.Height != ImagePreprocessor.Height || Preprocessing.Channels != 1)
        {
            throw new InvalidDataException("Unsupported preprocessing. Expected gray32-v1, 32x32, one channel.");
        }

        if (HiddenSize is < 1 or > 2048)
        {
            throw new InvalidDataException("hiddenSize must be between 1 and 2048.");
        }

        if (Labels is null || Labels.Length is < 2 or > 1000 || Labels.Any(string.IsNullOrWhiteSpace) ||
            Labels.Distinct(StringComparer.Ordinal).Count() != Labels.Length)
        {
            throw new InvalidDataException("Expected 2–1000 distinct, nonempty class labels.");
        }

        ValidateArray(HiddenWeights, checked(HiddenSize * ImagePreprocessor.InputSize), "hiddenWeights");
        ValidateArray(HiddenBiases, HiddenSize, "hiddenBiases");
        ValidateArray(OutputWeights, checked(Labels.Length * HiddenSize), "outputWeights");
        ValidateArray(OutputBiases, Labels.Length, "outputBiases");
    }

    private static void ValidateArray(double[]? values, int length, string name)
    {
        if (values is null || values.Length != length || values.Any(value => !double.IsFinite(value)))
        {
            throw new InvalidDataException($"Invalid {name}: expected {length} finite numbers.");
        }
    }
}

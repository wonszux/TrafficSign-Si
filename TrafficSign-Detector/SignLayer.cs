using System.Text.Json;

namespace TrafficSign_Detector;

public sealed class SignLayer
{
    public const int ImageSize = 48;
    public const int Channels = 3;
    public const int InputSize = ImageSize * ImageSize * Channels;
    public const int WeightCount = InputSize + 1;

    private readonly double[] _weights;

    private SignLayer(double[] weights, string? displayName)
    {
        _weights = weights;
        DisplayName = displayName;
    }

    public string? DisplayName { get; }

    public static SignLayer FromJson(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        string? displayName = null;
        JsonElement array = root;
        if (root.ValueKind != JsonValueKind.Array)
        {
            displayName = root.TryGetProperty("name", out JsonElement name) ? name.GetString() : null;
            array = root.GetProperty("weights");
        }

        double[]? weights = array.Deserialize<double[]>();
        if (weights is null || weights.Length != WeightCount || weights.Any(weight => !double.IsFinite(weight)))
        {
            throw new JsonException($"Expected {WeightCount} finite numbers.");
        }

        return new SignLayer(weights, string.IsNullOrWhiteSpace(displayName) ? null : displayName);
    }

    public double Forward(double[] inputs)
    {
        double sum = _weights[0];
        for (int index = 0; index < InputSize; index++)
        {
            sum += inputs[index] * _weights[index + 1];
        }

        return Sigmoid(sum);
    }

    private static double Sigmoid(double sum)
    {
        if (sum >= 0)
        {
            double z = Math.Exp(-sum);
            return 1.0 / (1.0 + z);
        }

        double negative = Math.Exp(sum);
        return negative / (1.0 + negative);
    }
}

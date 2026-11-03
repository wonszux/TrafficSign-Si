using System.Text.Json;

namespace TrafficSign_Detector;

public sealed class SignLayer
{
    public const int ImageSize = 48;
    public const int Channels = 3;
    public const int InputSize = ImageSize * ImageSize * Channels;
    public const int WeightCount = InputSize + 1;

    private readonly double[] _weights;

    private SignLayer(double[] weights)
    {
        _weights = weights;
    }

    public static SignLayer FromJson(string json)
    {
        double[]? weights = JsonSerializer.Deserialize<double[]>(json);
        if (weights is null || weights.Length != WeightCount || weights.Any(weight => !double.IsFinite(weight)))
        {
            throw new JsonException($"Expected {WeightCount} finite numbers.");
        }

        return new SignLayer(weights);
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

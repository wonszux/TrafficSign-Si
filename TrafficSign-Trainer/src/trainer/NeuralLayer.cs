namespace TrafficSign_Trainer.src.trainer
{
    public class NeuralLayer
    {
        public const int ImageSize = 48;
        public const int Channels = 3;
        public const int InputSize = ImageSize * ImageSize * Channels;
        public const int WeightCount = InputSize + 1;

        public double[] Weights { get; }

        public NeuralLayer(double[] weights)
        {
            ArgumentNullException.ThrowIfNull(weights);
            if (weights.Length != WeightCount || weights.Any(weight => !double.IsFinite(weight)))
            {
                throw new ArgumentException(
                    $"Weights must contain {WeightCount} finite numbers (bias, then {InputSize} pixel weights).");
            }

            Weights = weights;
        }

        public static double InitialWeightScale { get; } = 1.0 / Math.Sqrt(InputSize);

        public static NeuralLayer CreateRandom()
        {
            var weights = new double[WeightCount];
            for (int index = 0; index < weights.Length; index++)
            {
                weights[index] = (Random.Shared.NextDouble() * 2.0 - 1.0) * InitialWeightScale;
            }

            return new NeuralLayer(weights);
        }

        public double Forward(double[] inputs)
        {
            ArgumentNullException.ThrowIfNull(inputs);
            if (inputs.Length != InputSize)
            {
                throw new ArgumentException($"Expected {InputSize} inputs.");
            }

            double sum = Weights[0];
            for (int index = 0; index < InputSize; index++)
            {
                sum += inputs[index] * Weights[index + 1];
            }

            return Sigmoid(sum);
        }

        public void Learn(double[] inputs, double target, double learningRate)
        {
            double output = Forward(inputs);
            double delta = learningRate * (target - output) * output * (1.0 - output);
            Weights[0] += delta;
            for (int index = 0; index < InputSize; index++)
            {
                Weights[index + 1] += delta * inputs[index];
            }
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
}

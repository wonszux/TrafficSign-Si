namespace TrafficSign_Trainer.src.trainer
{
    public class NeuralNetwork
    {
        public readonly double[] weights;

        public NeuralNetwork(double[] weights)
        {
            ArgumentNullException.ThrowIfNull(weights);
            this.weights = weights;
        }
    }
}

using System.Text.Json;
using TrafficSign_Trainer.src.trainer;

public class Program
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".ppm", ".pgm"
    };

    static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: TrafficSign-Trainer <folder_zdjec> <plik_wag.json>");
            return 1;
        }

        try
        {
            string imagesDirectory = Path.GetFullPath(args[0]);
            string weightsPath = Path.GetFullPath(args[1]);

            string[] imageFiles = Directory.EnumerateFiles(imagesDirectory)
                .Where(path => ImageExtensions.Contains(Path.GetExtension(path)))
                .ToArray();
          
            using FileStream weightsFile = File.OpenRead(weightsPath);
            double[] weights = JsonSerializer.Deserialize<double[]>(weightsFile)
            ?? throw new JsonException("Weights cannot be null.");

            var network = new NeuralNetwork(weights);

            Console.WriteLine($"Photo directory: {imagesDirectory}");
            Console.WriteLine($"Found images: {imageFiles.Length}");
            Console.WriteLine($"Loaded weights: {network.weights.Length}");
            return 0;
        }
        catch (JsonException)
        {
            Console.Error.WriteLine("Invalid JSON file with weights. Expected array of numbers, e.g., [0.1, -0.2, 0.3].");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Console.Error.WriteLine($"Error reading input data: {exception.Message}");
        }

        return 1;
    }
}

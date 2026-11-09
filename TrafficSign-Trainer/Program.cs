using System.Globalization;
using System.Text.Json;

namespace TrafficSign.Trainer;

public static class Program
{
    private const string Usage = "Usage: TrafficSign-Trainer <data-folder> <model.json> [--epochs N] [--learning-rate X] [--hidden-size N] [--seed N] [--split-manifest split.json]\n       TrafficSign-Trainer evaluate <model.json> <data-folder> <saved.split.json>";

    public static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            if (args.Length == 1 && args[0] is "--help" or "-h")
            {
                output.WriteLine(Usage);
                return 0;
            }
            if (args.Length > 0 && args[0] == "evaluate")
            {
                if (args.Length != 4) throw new ArgumentException(Usage);
                Training.EvaluateSaved(args[1], args[2], args[3], output);
                return 0;
            }
            if (args.Length < 2) throw new ArgumentException(Usage);
            var options = new TrainingOptions();
            string? manifest = null;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 2; i < args.Length; i += 2)
            {
                if (i + 1 >= args.Length) throw new ArgumentException($"Missing value for {args[i]}.");
                if (!seen.Add(args[i])) throw new ArgumentException($"Duplicate option: {args[i]}.");
                string value = args[i + 1];
                options = args[i] switch
                {
                    "--epochs" => options with { Epochs = ParseInteger(args[i], value) },
                    "--learning-rate" => options with { LearningRate = ParseRate(value) },
                    "--hidden-size" => options with { HiddenSize = ParseInteger(args[i], value) },
                    "--seed" => options with { Seed = ParseInteger(args[i], value) },
                    "--split-manifest" => options,
                    _ => throw new ArgumentException($"Unknown option: {args[i]}.")
                };
                if (args[i] == "--split-manifest") manifest = value;
            }
            Training.Run(args[0], args[1], manifest, options, output);
            return 0;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException
            or JsonException or FormatException or OverflowException or ArithmeticException or NotSupportedException)
        {
            error.WriteLine($"Error: {exception.Message}");
            if (exception.Data["ModelTemporaryCleanupFailure"] is string cleanupMessage)
            {
                error.WriteLine($"Warning: {cleanupMessage}");
            }
            return 1;
        }
    }

    private static int ParseInteger(string option, string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed : throw new ArgumentException($"Invalid value for {option}: '{value}'. Expected an integer.");

    private static double ParseRate(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed : throw new ArgumentException($"Invalid value for --learning-rate: '{value}'. Use a decimal point.");
}

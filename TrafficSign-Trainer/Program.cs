using System.Globalization;
using System.Text.Json;

namespace TrafficSign.Trainer;

/// <summary>Command-line entry point for training with final held-out evaluation.</summary>
public static class Program
{
    private const string Usage = "Usage: TrafficSign-Trainer <data-folder> <model.json> [--epochs N] " +
        "[--learning-rate X] [--hidden-size N] [--seed N] [--split-manifest split.json]";

    public static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    /// <summary>Executes a command, reporting expected input, file and numeric failures to the error writer.</summary>
    /// <returns>Zero on success (including help), or one for an expected failure.</returns>
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        try
        {
            if (args.Length == 1 && args[0] is "--help" or "-h")
            {
                output.WriteLine(Usage);
                return 0;
            }
            if (args.Length < 2)
            {
                throw new ArgumentException(Usage);
            }

            var (options, manifestPath) = ParseTrainingOptions(args.AsSpan(2));
            Training.Run(args[0], args[1], manifestPath, options, output);
            return 0;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException
            or JsonException or FormatException or ArithmeticException or NotSupportedException)
        {
            error.WriteLine($"Error: {exception.Message}");
            if (exception.Data["ModelTemporaryCleanupFailure"] is string cleanupMessage)
            {
                error.WriteLine($"Warning: {cleanupMessage}");
            }
            return 1;
        }
    }

    private static (TrainingOptions Options, string? ManifestPath) ParseTrainingOptions(ReadOnlySpan<string> arguments)
    {
        var options = new TrainingOptions();
        string? manifestPath = null;
        var seenOptions = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < arguments.Length; index += 2)
        {
            string option = arguments[index];
            if (index + 1 >= arguments.Length)
            {
                throw new ArgumentException($"Missing value for {option}.");
            }
            if (!seenOptions.Add(option))
            {
                throw new ArgumentException($"Duplicate option: {option}.");
            }

            string value = arguments[index + 1];
            if (option == "--split-manifest")
            {
                manifestPath = value;
                continue;
            }

            options = option switch
            {
                "--epochs" => options with { Epochs = ParseInteger(option, value) },
                "--learning-rate" => options with { LearningRate = ParseRate(value) },
                "--hidden-size" => options with { HiddenSize = ParseInteger(option, value) },
                "--seed" => options with { Seed = ParseInteger(option, value) },
                _ => throw new ArgumentException($"Unknown option: {option}.")
            };
        }
        return (options, manifestPath);
    }

    private static int ParseInteger(string option, string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed : throw new ArgumentException($"Invalid value for {option}: '{value}'. Expected an integer.");

    private static double ParseRate(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed : throw new ArgumentException($"Invalid value for --learning-rate: '{value}'. Use a decimal point.");
}

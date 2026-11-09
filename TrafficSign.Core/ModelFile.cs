using System.Text.Json;

namespace TrafficSign.Core;

/// <summary>Reads validated models and saves new models without overwriting existing files.</summary>
public static class ModelFile
{
    private const long MaximumFileSizeBytes = 128L * 1024 * 1024;

    /// <summary>Shared camelCase serialization settings for the model and experiment file contracts.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>Reads a model of at most 128 MiB and validates its version and parameters.</summary>
    public static ModelData Load(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > MaximumFileSizeBytes)
        {
            throw new InvalidDataException("Model exceeds the 128 MiB limit.");
        }

        var model = JsonSerializer.Deserialize<ModelData>(stream, JsonOptions)
            ?? throw new InvalidDataException("Model cannot be null.");
        model.Validate();
        return model;
    }

    /// <summary>Validates and publishes a model by moving a temporary file in the target directory.</summary>
    /// <exception cref="IOException">The destination already exists, or writing or moving the file fails.</exception>
    public static void Save(string path, ModelData model)
    {
        ArgumentNullException.ThrowIfNull(model);
        model.Validate();
        string targetPath = Path.GetFullPath(path);
        string targetDirectory = Path.GetDirectoryName(targetPath)
            ?? throw new ArgumentException("Model path must name a file.", nameof(path));
        Directory.CreateDirectory(targetDirectory);
        string temporaryPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write))
            {
                JsonSerializer.Serialize(stream, model, JsonOptions);
            }

            File.Move(temporaryPath, targetPath, overwrite: false);
        }
        catch (Exception failure)
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception cleanupFailure) when (cleanupFailure is IOException or UnauthorizedAccessException)
            {
                failure.Data["ModelTemporaryCleanupFailure"] = $"Could not remove temporary model: {temporaryPath}";
                failure.Data["ModelTemporaryCleanupException"] = cleanupFailure;
            }
            throw;
        }
    }
}

using System.Security.Cryptography;
using System.Text.Json;
using TrafficSign.Core;

namespace TrafficSign.Trainer;

internal sealed record ExperimentPaths(string Model, string Report, string Split, string TestReport)
{
    public static ExperimentPaths ForModel(string modelPath)
    {
        string fullModelPath = Path.GetFullPath(modelPath);
        string directory = Path.GetDirectoryName(fullModelPath)
            ?? throw new ArgumentException("Model path must name a file.", nameof(modelPath));
        string stem = Path.Combine(directory, Path.GetFileNameWithoutExtension(fullModelPath));
        return new ExperimentPaths(fullModelPath, stem + ".report.json", stem + ".split.json", stem + ".test-report.json");
    }

    public void EnsureTrainingOutputsAvailable() => ExperimentFiles.EnsureAvailable(Model, Report, Split);
}

internal static class ExperimentFiles
{
    public static void EnsureAvailable(params string[] paths)
    {
        if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length ||
            paths.Any(path => File.Exists(path) || Directory.Exists(path)))
        {
            throw new IOException("An output path already exists or output paths collide. Choose a new model name.");
        }
    }

    public static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static void VerifyIdentity(ExperimentPaths paths, Dataset dataset, string modelHash)
    {
        using var stream = File.OpenRead(paths.Report);
        var identity = JsonSerializer.Deserialize<TrainingReportIdentity>(stream, ModelFile.JsonOptions);
        if (identity is null || identity.FormatVersion != 1 || identity.ModelSha256 != modelHash ||
            identity.DatasetFingerprint != dataset.Fingerprint)
        {
            throw new InvalidDataException("Model or split does not match the original training report.");
        }
    }

    public static void PublishTraining(
        ExperimentPaths paths, ModelData model, SplitManifest split, TrainingReport report, TextWriter output)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(paths.Model)!);
        string temporaryStem = paths.Model + "." + Guid.NewGuid().ToString("N");
        string modelTemporary = temporaryStem + ".model.tmp";
        string reportTemporary = temporaryStem + ".report.tmp";
        string splitTemporary = temporaryStem + ".split.tmp";
        string[] temporaries = [modelTemporary, reportTemporary, splitTemporary];
        var published = new List<string>();
        try
        {
            ModelFile.Save(modelTemporary, model);
            WriteJsonNew(reportTemporary, report with { ModelSha256 = HashFile(modelTemporary) });
            WriteJsonNew(splitTemporary, split);
            Publish(splitTemporary, paths.Split);
            Publish(reportTemporary, paths.Report);
            Publish(modelTemporary, paths.Model); // The model marks a complete experiment.
        }
        catch (Exception failure)
        {
            Cleanup(published.Concat(temporaries), output, failure);
            throw;
        }

        void Publish(string temporary, string target)
        {
            File.Move(temporary, target, overwrite: false);
            published.Add(target);
        }
    }

    public static void PublishTestReport(string path, TestReport report, TextWriter output)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            WriteJsonNew(temporary, report);
            File.Move(temporary, path, overwrite: false);
        }
        catch (Exception failure)
        {
            Cleanup([temporary], output, failure);
            throw;
        }
    }

    private static void WriteJsonNew<T>(string path, T value)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        JsonSerializer.Serialize(stream, value, ModelFile.JsonOptions);
    }

    private static void Cleanup(IEnumerable<string> paths, TextWriter output, Exception originalFailure)
    {
        var remainingPaths = new List<string>();
        var cleanupFailures = new List<Exception>();
        foreach (string path in paths)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception cleanupFailure) when (cleanupFailure is IOException or UnauthorizedAccessException)
            {
                remainingPaths.Add(path);
                cleanupFailures.Add(cleanupFailure);
            }
        }
        if (remainingPaths.Count == 0)
        {
            return;
        }

        string message = "Cleanup could not remove: " + string.Join(", ", remainingPaths);
        originalFailure.Data["CleanupFailures"] = message;
        originalFailure.Data["CleanupExceptions"] = new AggregateException(cleanupFailures);
        try
        {
            output.WriteLine("Warning: " + message);
        }
        catch (Exception diagnosticFailure)
        {
            // A failed diagnostic writer must not replace the publication failure.
            originalFailure.Data["CleanupDiagnosticFailure"] = diagnosticFailure;
        }
    }
}

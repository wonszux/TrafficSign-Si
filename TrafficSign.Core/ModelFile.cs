using System.Text.Json;

namespace TrafficSign.Core;

public static class ModelFile
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static ModelData Load(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > 128 * 1024 * 1024) throw new InvalidDataException("Model exceeds the 128 MiB limit.");
        var model = JsonSerializer.Deserialize<ModelData>(stream, JsonOptions)
            ?? throw new InvalidDataException("Model cannot be null.");
        model.Validate();
        return model;
    }

    public static void Save(string path, ModelData model)
    {
        model.Validate();
        string target = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                JsonSerializer.Serialize(stream, model, JsonOptions);
            File.Move(temporary, target, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

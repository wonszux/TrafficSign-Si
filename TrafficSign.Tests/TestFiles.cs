using SkiaSharp;

namespace TrafficSign.Tests;

internal sealed class TestFiles : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "TrafficSign.Tests", Guid.NewGuid().ToString("N"));
    public TestFiles() => Directory.CreateDirectory(Root);
    public string PathFor(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
    public void Dispose() => Directory.Delete(Root, recursive: true);

    public string Image(string relative, int value, bool vertical = false)
    {
        string path = PathFor(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = new SKBitmap(32, 32, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        bitmap.Erase(new SKColor((byte)value, (byte)value, (byte)value));
        for (int i = 0; i < 32; i++)
            bitmap.SetPixel(vertical ? 16 : i, vertical ? i : 16, SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        encoded.SaveTo(stream);
        return path;
    }

    public string Dataset(int count = 10)
    {
        for (int i = 0; i < count; i++)
        {
            Image($"data/dark/{i:D2}.png", 5 + i);
            Image($"data/light/{i:D2}.png", 230 + i, vertical: true);
        }
        return PathFor("data");
    }
}

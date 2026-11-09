using SkiaSharp;
using TrafficSign.Core;
using Xunit;

namespace TrafficSign.Tests;

public sealed class PreprocessingTests
{
    [Fact]
    public void GrayscaleCompositesAlphaAndIsRepeatable()
    {
        using var files = new TestFiles();
        string path = files.PathFor("alpha.png");
        Save(path, new SKColor(255, 0, 0, 128));
        var first = ImagePreprocessor.Process(path);
        var second = ImagePreprocessor.Process(path);
        Assert.Equal(ImagePreprocessor.InputSize, first.Length);
        Assert.Equal(first, second);
        double expected = 0.299 * (128 / 255.0) + 127 / 255.0;
        Assert.All(first, value => Assert.InRange(value, expected - 1e-8, expected + 1e-8));
    }

    [Fact]
    public void BilinearUsesPixelCentersAndRowMajorOrder()
    {
        using var files = new TestFiles();
        string path = files.PathFor("ramp.png");
        using (var bitmap = new SKBitmap(2, 1))
        {
            bitmap.SetPixel(0, 0, SKColors.Black);
            bitmap.SetPixel(1, 0, SKColors.White);
            SaveBitmap(bitmap, path, SKEncodedImageFormat.Png);
        }
        var input = ImagePreprocessor.Process(path);
        Assert.Equal(0, input[0]);
        Assert.Equal(0.03125, input[8], 10);
        Assert.Equal(0.53125, input[16], 10);
        Assert.Equal(1, input[31], 10);
        Assert.Equal(input.Take(32), input.Skip(32).Take(32));
    }

    [Fact]
    public void JpegExifRotationIsAppliedBeforeResize()
    {
        using var files = new TestFiles();
        string path = files.PathFor("oriented.jpg");
        using (var bitmap = new SKBitmap(32, 16))
        {
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 32; x++) bitmap.SetPixel(x, y, x < 16 ? SKColors.Black : SKColors.White);
            SaveBitmap(bitmap, path, SKEncodedImageFormat.Jpeg);
        }
        byte[] jpeg = File.ReadAllBytes(path);
        // EXIF little-endian IFD: Orientation (0x0112), SHORT, one value, rotate 90 degrees clockwise.
        byte[] exif = [0xff, 0xe1, 0x00, 0x22, 0x45, 0x78, 0x69, 0x66, 0, 0,
            0x49, 0x49, 0x2a, 0, 8, 0, 0, 0, 1, 0, 0x12, 0x01, 3, 0, 1, 0, 0, 0, 6, 0, 0, 0, 0, 0, 0, 0];
        File.WriteAllBytes(path, [.. jpeg.Take(2), .. exif, .. jpeg.Skip(2)]);
        var input = ImagePreprocessor.Process(path);
        Assert.All(input.Take(32), value => Assert.InRange(value, 0, 0.02));
        Assert.All(input.Skip(31 * 32), value => Assert.InRange(value, 0.98, 1));
    }

    [Fact]
    public void DamagedOrMislabeledFilesAreRejected()
    {
        using var files = new TestFiles();
        string path = files.PathFor("broken.png");
        File.WriteAllText(path, "not an image");
        Assert.Throws<InvalidDataException>(() => ImagePreprocessor.Process(path));
        Assert.Throws<InvalidDataException>(() => ImagePreprocessor.Process(files.PathFor("photo.gif")));
    }

    private static void Save(string path, SKColor color)
    {
        using var bitmap = new SKBitmap(1, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        bitmap.SetPixel(0, 0, color);
        SaveBitmap(bitmap, path, SKEncodedImageFormat.Png);
    }

    private static void SaveBitmap(SKBitmap bitmap, string path, SKEncodedImageFormat format)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(format, 100);
        using var stream = File.Create(path);
        encoded.SaveTo(stream);
    }
}

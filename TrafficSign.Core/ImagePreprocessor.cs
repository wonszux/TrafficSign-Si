using SkiaSharp;

namespace TrafficSign.Core;

public static class ImagePreprocessor
{
    public const int Width = 32;
    public const int Height = 32;
    public const int InputSize = Width * Height;
    public const long MaximumPixels = 20_000_000;

    public static bool IsSupportedPath(string path) =>
        Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".jpeg", StringComparison.OrdinalIgnoreCase);

    public static double[] Process(string path)
    {
        if (!IsSupportedPath(path)) throw new InvalidDataException("Only JPEG and PNG images are supported.");
        using var stream = File.OpenRead(path);
        using var managed = new SKManagedStream(stream);
        using var codec = SKCodec.Create(managed) ?? throw new InvalidDataException($"Cannot decode image: {path}");
        if (codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png))
            throw new InvalidDataException($"File content is not JPEG or PNG: {path}");
        if (codec.FrameCount > 1) throw new InvalidDataException($"Animated images are unsupported: {path}");
        int sourceWidth = codec.Info.Width, sourceHeight = codec.Info.Height;
        if (sourceWidth <= 0 || sourceHeight <= 0 || (long)sourceWidth * sourceHeight > MaximumPixels)
            throw new InvalidDataException($"Image dimensions exceed the {MaximumPixels} pixel limit: {path}");
        using var bitmap = new SKBitmap(sourceWidth, sourceHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var status = codec.GetPixels(bitmap.Info, bitmap.GetPixels());
        if (status != SKCodecResult.Success) throw new InvalidDataException($"Incomplete or damaged image ({status}): {path}");
        int origin = (int)codec.EncodedOrigin;
        bool rotated = origin is >= 5 and <= 8;
        int orientedWidth = rotated ? sourceHeight : sourceWidth;
        int orientedHeight = rotated ? sourceWidth : sourceHeight;
        var result = new double[InputSize];
        for (int y = 0; y < Height; y++)
        {
            double sourceY = Math.Clamp((y + 0.5) * orientedHeight / Height - 0.5, 0, orientedHeight - 1);
            int y0 = (int)sourceY, y1 = Math.Min(y0 + 1, orientedHeight - 1);
            double fy = sourceY - y0;
            for (int x = 0; x < Width; x++)
            {
                double sourceX = Math.Clamp((x + 0.5) * orientedWidth / Width - 0.5, 0, orientedWidth - 1);
                int x0 = (int)sourceX, x1 = Math.Min(x0 + 1, orientedWidth - 1);
                double fx = sourceX - x0;
                double top = Gray(x0, y0) * (1 - fx) + Gray(x1, y0) * fx;
                double bottom = Gray(x0, y1) * (1 - fx) + Gray(x1, y1) * fx;
                result[y * Width + x] = (top * (1 - fy) + bottom * fy) / 255;
            }
        }
        return result;

        double Gray(int x, int y)
        {
            (int sx, int sy) = origin switch
            {
                2 => (sourceWidth - 1 - x, y),
                3 => (sourceWidth - 1 - x, sourceHeight - 1 - y),
                4 => (x, sourceHeight - 1 - y),
                5 => (y, x),
                6 => (y, sourceHeight - 1 - x),
                7 => (sourceWidth - 1 - y, sourceHeight - 1 - x),
                8 => (sourceWidth - 1 - y, x),
                _ => (x, y)
            };
            SKColor color = bitmap.GetPixel(sx, sy);
            double alpha = color.Alpha / 255.0;
            // Composite before interpolation; grayscale is linear, so no intermediate byte rounding.
            return (0.299 * color.Red + 0.587 * color.Green + 0.114 * color.Blue) * alpha + 255 * (1 - alpha);
        }
    }
}

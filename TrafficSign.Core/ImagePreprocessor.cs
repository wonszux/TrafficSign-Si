using SkiaSharp;

namespace TrafficSign.Core;

/// <summary>Implements gray32-v1: EXIF orientation, white alpha compositing, bilinear resize and grayscale.</summary>
public static class ImagePreprocessor
{
    public const int Width = 32;
    public const int Height = 32;
    public const int InputSize = Width * Height;
    public const long MaximumPixels = 20_000_000;

    /// <summary>Checks the filename extension only; <see cref="Process"/> also validates the encoded content.</summary>
    public static bool IsSupportedPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string extension = Path.GetExtension(path);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Decodes a static JPEG or PNG into a normalized, row-major vector of 32 × 32 pixels.</summary>
    /// <returns>Grayscale values in [0, 1], without intermediate byte rounding.</returns>
    /// <exception cref="InvalidDataException">The image is unsupported, damaged, animated or exceeds the pixel limit.</exception>
    public static double[] Process(string path)
    {
        if (!IsSupportedPath(path))
        {
            throw new InvalidDataException("Only JPEG and PNG images are supported.");
        }

        using var stream = File.OpenRead(path);
        using var managedStream = new SKManagedStream(stream);
        using var codec = SKCodec.Create(managedStream) ?? throw new InvalidDataException($"Cannot decode image: {path}");
        if (codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png))
        {
            throw new InvalidDataException($"File content is not JPEG or PNG: {path}");
        }

        if (codec.FrameCount > 1)
        {
            throw new InvalidDataException($"Animated images are unsupported: {path}");
        }

        int sourceWidth = codec.Info.Width;
        int sourceHeight = codec.Info.Height;
        if (sourceWidth <= 0 || sourceHeight <= 0 || (long)sourceWidth * sourceHeight > MaximumPixels)
        {
            throw new InvalidDataException($"Image dimensions exceed the {MaximumPixels} pixel limit: {path}");
        }

        using var bitmap = new SKBitmap(sourceWidth, sourceHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var status = codec.GetPixels(bitmap.Info, bitmap.GetPixels());
        if (status != SKCodecResult.Success)
        {
            throw new InvalidDataException($"Incomplete or damaged image ({status}): {path}");
        }

        return ResizeToGrayscale(bitmap, codec.EncodedOrigin);
    }

    private static double[] ResizeToGrayscale(SKBitmap bitmap, SKEncodedOrigin origin)
    {
        bool swapsDimensions = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        int orientedWidth = swapsDimensions ? bitmap.Height : bitmap.Width;
        int orientedHeight = swapsDimensions ? bitmap.Width : bitmap.Height;
        var result = new double[InputSize];
        for (int y = 0; y < Height; y++)
        {
            double sourceY = Math.Clamp((y + 0.5) * orientedHeight / Height - 0.5, 0, orientedHeight - 1);
            int topY = (int)sourceY;
            int bottomY = Math.Min(topY + 1, orientedHeight - 1);
            double verticalFraction = sourceY - topY;
            for (int x = 0; x < Width; x++)
            {
                double sourceX = Math.Clamp((x + 0.5) * orientedWidth / Width - 0.5, 0, orientedWidth - 1);
                int leftX = (int)sourceX;
                int rightX = Math.Min(leftX + 1, orientedWidth - 1);
                double horizontalFraction = sourceX - leftX;
                double top = ReadGrayscale(leftX, topY) * (1 - horizontalFraction) + ReadGrayscale(rightX, topY) * horizontalFraction;
                double bottom = ReadGrayscale(leftX, bottomY) * (1 - horizontalFraction) + ReadGrayscale(rightX, bottomY) * horizontalFraction;
                result[y * Width + x] = (top * (1 - verticalFraction) + bottom * verticalFraction) / 255;
            }
        }
        return result;

        double ReadGrayscale(int x, int y)
        {
            (int sourceX, int sourceY) = origin switch
            {
                SKEncodedOrigin.TopRight => (bitmap.Width - 1 - x, y),
                SKEncodedOrigin.BottomRight => (bitmap.Width - 1 - x, bitmap.Height - 1 - y),
                SKEncodedOrigin.BottomLeft => (x, bitmap.Height - 1 - y),
                SKEncodedOrigin.LeftTop => (y, x),
                SKEncodedOrigin.RightTop => (y, bitmap.Height - 1 - x),
                SKEncodedOrigin.RightBottom => (bitmap.Width - 1 - y, bitmap.Height - 1 - x),
                SKEncodedOrigin.LeftBottom => (bitmap.Width - 1 - y, x),
                _ => (x, y)
            };
            SKColor color = bitmap.GetPixel(sourceX, sourceY);
            double alpha = color.Alpha / 255.0;
            // Composite before interpolation; grayscale is linear, so no intermediate byte rounding.
            return (0.299 * color.Red + 0.587 * color.Green + 0.114 * color.Blue) * alpha + 255 * (1 - alpha);
        }
    }
}

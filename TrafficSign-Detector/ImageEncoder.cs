using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace TrafficSign_Detector;

public static class ImageEncoder
{
    public const int ImageSize = SignLayer.ImageSize;

    public static async Task<double[]> EncodeAsync(StorageFile file)
    {
        using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read);
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
        var transform = new BitmapTransform
        {
            ScaledWidth = ImageSize,
            ScaledHeight = ImageSize,
            InterpolationMode = BitmapInterpolationMode.Fant
        };
        PixelDataProvider pixels = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Straight,
            transform,
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.DoNotColorManage);
        byte[] bytes = pixels.DetachPixelData();
        int pixelCount = ImageSize * ImageSize;
        if (bytes.Length < pixelCount * 4)
        {
            throw new InvalidDataException($"The image could not be resized to {ImageSize}×{ImageSize}.");
        }

        var inputs = new double[pixelCount * SignLayer.Channels];
        for (int index = 0; index < pixelCount; index++)
        {
            int offset = index * 4;
            int pixel = index * SignLayer.Channels;
            inputs[pixel] = bytes[offset + 2] / 255.0;
            inputs[pixel + 1] = bytes[offset + 1] / 255.0;
            inputs[pixel + 2] = bytes[offset] / 255.0;
        }

        return inputs;
    }
}

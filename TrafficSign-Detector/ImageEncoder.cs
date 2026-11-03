using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace TrafficSign_Detector;

public static class ImageEncoder
{
    public static async Task<DecodedImage> DecodeAsync(StorageFile file)
    {
        using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read);
        return await DecodeAsync(stream);
    }

    public static async Task<DecodedImage> DecodeAsync(IRandomAccessStream stream)
    {
        stream.Seek(0);
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
        PixelDataProvider pixels = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Straight,
            new BitmapTransform(),
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.DoNotColorManage);
        byte[] bytes = pixels.DetachPixelData();
        int width = (int)decoder.OrientedPixelWidth;
        int height = (int)decoder.OrientedPixelHeight;
        if (bytes.Length < width * height * 4)
        {
            throw new InvalidDataException("The image could not be read.");
        }

        return new DecodedImage(bytes, width, height);
    }
}

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace TrafficSign_Trainer.src.trainer
{
    public readonly record struct TrainingSample(double[] Inputs, double Target);

    public sealed record TrainingSet(TrainingSample[] Samples, int PositiveCount, int NegativeCount);

    public sealed record ClassImages(string Name, double[][] Inputs);

    public static class SampleLoader
    {
        public const int ImageSize = NeuralLayer.ImageSize;

        public static TrainingSet Load(string positiveDirectory, IReadOnlySet<string> imageExtensions)
        {
            string positiveFullPath = Path.GetFullPath(positiveDirectory);
            string? parentDirectory = Path.GetDirectoryName(positiveFullPath);
            if (string.IsNullOrEmpty(parentDirectory))
            {
                throw new IOException($"Cannot find sibling folders for '{positiveFullPath}'.");
            }

            TrainingSample[] positives = LoadDirectory(positiveFullPath, 1.0, imageExtensions);
            if (positives.Length == 0)
            {
                throw new IOException($"No images in the target folder '{positiveFullPath}'.");
            }

            var negatives = new List<TrainingSample>();
            foreach (string sibling in Directory.EnumerateDirectories(parentDirectory))
            {
                if (string.Equals(Path.GetFullPath(sibling), positiveFullPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                negatives.AddRange(LoadDirectory(sibling, 0.0, imageExtensions));
            }

            if (negatives.Count == 0)
            {
                throw new IOException(
                    $"No images in sibling folders of '{positiveFullPath}'. The layer needs negative examples.");
            }

            var samples = new TrainingSample[positives.Length + negatives.Count];
            positives.CopyTo(samples, 0);
            negatives.CopyTo(samples, positives.Length);
            return new TrainingSet(samples, positives.Length, negatives.Count);
        }

        public static ClassImages[] LoadClasses(string root, IReadOnlySet<string> imageExtensions)
        {
            var classes = new List<ClassImages>();
            foreach (string directory in Directory.EnumerateDirectories(root).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                string name = Path.GetFileName(directory);
                if (name.Equals("weights", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("background", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                TrainingSample[] samples = LoadDirectory(directory, 1.0, imageExtensions);
                if (samples.Length == 0)
                {
                    continue;
                }

                classes.Add(new ClassImages(name, samples.Select(sample => sample.Inputs).ToArray()));
            }

            if (classes.Count == 0)
            {
                throw new IOException($"No class folders with images in '{root}'.");
            }

            return classes.ToArray();
        }

        public static double[][] LoadInputs(string directory, IReadOnlySet<string> imageExtensions)
        {
            if (!Directory.Exists(directory))
            {
                return [];
            }

            return LoadDirectory(directory, 0.0, imageExtensions)
                .Select(sample => sample.Inputs)
                .ToArray();
        }

        private static TrainingSample[] LoadDirectory(
            string directory,
            double target,
            IReadOnlySet<string> imageExtensions)
        {
            return Directory.EnumerateFiles(directory)
                .Where(path => imageExtensions.Contains(Path.GetExtension(path)))
                .Select(path => new TrainingSample(Encode(path), target))
                .ToArray();
        }

        private static double[] Encode(string path)
        {
            try
            {
                using var source = new Bitmap(path);
                using var resized = new Bitmap(ImageSize, ImageSize, PixelFormat.Format24bppRgb);
                using (var graphics = Graphics.FromImage(resized))
                {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.DrawImage(source, 0, 0, ImageSize, ImageSize);
                }

                var inputs = new double[NeuralLayer.InputSize];
                for (int y = 0; y < ImageSize; y++)
                {
                    for (int x = 0; x < ImageSize; x++)
                    {
                        Color color = resized.GetPixel(x, y);
                        int pixel = (y * ImageSize + x) * NeuralLayer.Channels;
                        inputs[pixel] = color.R / 255.0;
                        inputs[pixel + 1] = color.G / 255.0;
                        inputs[pixel + 2] = color.B / 255.0;
                    }
                }

                return inputs;
            }
            catch (Exception exception) when (exception is ArgumentException
                or IOException
                or NotSupportedException
                or OutOfMemoryException
                or System.Runtime.InteropServices.ExternalException)
            {
                throw new IOException($"Cannot decode image '{path}': {exception.Message}", exception);
            }
        }
    }
}

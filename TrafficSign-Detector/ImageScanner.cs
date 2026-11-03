namespace TrafficSign_Detector;

public readonly record struct NeuronHit(double Score, int X, int Y, int Side);

public sealed class ScanResult
{
    public ScanResult(NeuronHit[] hits, int answerNeuron, NeuronHit answer)
    {
        Hits = hits;
        AnswerNeuron = answerNeuron;
        Answer = answer;
    }

    public NeuronHit[] Hits { get; }

    public int AnswerNeuron { get; }

    public NeuronHit Answer { get; }
}

public sealed class DecodedImage
{
    public DecodedImage(byte[] bgra, int width, int height)
    {
        if (width <= 0 || height <= 0 || bgra.Length < width * height * 4)
        {
            throw new InvalidDataException("The image has no pixels to scan.");
        }

        Bgra = bgra;
        Width = width;
        Height = height;
    }

    public byte[] Bgra { get; }

    public int Width { get; }

    public int Height { get; }
}

public static class ImageScanner
{
    private static readonly int[] BaseSides = [96, 128, 192, 256];

    public static ScanResult Scan(DecodedImage image, IReadOnlyList<SignLayer> layers)
    {
        var hits = new NeuronHit[layers.Count];
        if (layers.Count == 0)
        {
            return new ScanResult(hits, -1, default);
        }

        int answerNeuron = -1;
        NeuronHit answer = default;
        int wholeNeuron = -1;
        NeuronHit wholeHit = default;
        double wholeMargin = 0;
        double imageArea = image.Width * (double)image.Height;
        var inputs = new double[SignLayer.InputSize];
        foreach (int side in SidesFor(image.Width, image.Height))
        {
            int step = Math.Max(1, side / 4);
            foreach (int y in Axis(image.Height, side, step))
            {
                foreach (int x in Axis(image.Width, side, step))
                {
                    FillWindow(image, x, y, side, inputs);
                    int best = 0;
                    double bestScore = layers[0].Forward(inputs);
                    Record(hits, 0, bestScore, x, y, side);
                    double secondScore = 0;
                    for (int index = 1; index < layers.Count; index++)
                    {
                        double score = layers[index].Forward(inputs);
                        Record(hits, index, score, x, y, side);
                        if (score > bestScore)
                        {
                            secondScore = bestScore;
                            best = index;
                            bestScore = score;
                        }
                        else if (score > secondScore)
                        {
                            secondScore = score;
                        }
                    }

                    if (answerNeuron < 0 || bestScore > answer.Score)
                    {
                        answerNeuron = best;
                        answer = new NeuronHit(bestScore, x, y, side);
                    }

                    if (side * (double)side >= imageArea * 0.8)
                    {
                        double margin = bestScore - secondScore;
                        if (margin > wholeMargin)
                        {
                            wholeMargin = margin;
                            wholeNeuron = best;
                            wholeHit = new NeuronHit(bestScore, x, y, side);
                        }
                    }
                }
            }
        }

        if (wholeNeuron >= 0 && wholeMargin >= 0.5)
        {
            return new ScanResult(hits, wholeNeuron, wholeHit);
        }

        return new ScanResult(hits, answerNeuron, answer);
    }

    public static double[] ScoreWindow(DecodedImage image, IReadOnlyList<SignLayer> layers, int x, int y, int side)
    {
        var scores = new double[layers.Count];
        if (layers.Count == 0)
        {
            return scores;
        }

        int limit = Math.Min(image.Width, image.Height);
        side = Math.Clamp(side, 1, Math.Max(1, limit));
        x = Math.Clamp(x, 0, Math.Max(0, image.Width - side));
        y = Math.Clamp(y, 0, Math.Max(0, image.Height - side));
        var inputs = new double[SignLayer.InputSize];
        FillWindow(image, x, y, side, inputs);
        for (int index = 0; index < layers.Count; index++)
        {
            scores[index] = layers[index].Forward(inputs);
        }

        return scores;
    }

    public static int[] SidesFor(int width, int height)
    {
        int longSide = Math.Max(width, height);
        int shortSide = Math.Min(width, height);
        if (shortSide <= 0)
        {
            return [];
        }

        double scale = Math.Max(longSide, 600) / 600.0;
        var sides = new List<int>();
        foreach (int basis in BaseSides)
        {
            int side = (int)Math.Round(basis * scale);
            if (side >= SignLayer.ImageSize && side <= shortSide && !sides.Contains(side))
            {
                sides.Add(side);
            }
        }

        if (!sides.Contains(shortSide))
        {
            sides.Add(shortSide);
        }

        sides.Sort();
        return sides.ToArray();
    }

    private static void Record(NeuronHit[] hits, int neuron, double score, int x, int y, int side)
    {
        if (score > hits[neuron].Score)
        {
            hits[neuron] = new NeuronHit(score, x, y, side);
        }
    }

    private static IEnumerable<int> Axis(int length, int side, int step)
    {
        if (length <= side)
        {
            yield return 0;
            yield break;
        }

        int previous = -1;
        for (int position = 0; position + side <= length; position += step)
        {
            yield return position;
            previous = position;
        }

        int end = length - side;
        if (end != previous)
        {
            yield return end;
        }
    }

    private static void FillWindow(DecodedImage image, int cropX, int cropY, int side, double[] inputs)
    {
        double scale = side / (double)SignLayer.ImageSize;
        for (int dy = 0; dy < SignLayer.ImageSize; dy++)
        {
            int y0 = cropY + (int)(dy * scale);
            int y1 = cropY + (int)((dy + 1) * scale);
            if (y1 <= y0)
            {
                y1 = y0 + 1;
            }

            int yLimit = Math.Min(cropY + side, image.Height);
            if (y1 > yLimit)
            {
                y1 = yLimit;
            }

            for (int dx = 0; dx < SignLayer.ImageSize; dx++)
            {
                int x0 = cropX + (int)(dx * scale);
                int x1 = cropX + (int)((dx + 1) * scale);
                if (x1 <= x0)
                {
                    x1 = x0 + 1;
                }

                int xLimit = Math.Min(cropX + side, image.Width);
                if (x1 > xLimit)
                {
                    x1 = xLimit;
                }

                long red = 0;
                long green = 0;
                long blue = 0;
                int count = 0;
                for (int y = y0; y < y1; y++)
                {
                    int row = y * image.Width * 4;
                    for (int x = x0; x < x1; x++)
                    {
                        int offset = row + (x * 4);
                        blue += image.Bgra[offset];
                        green += image.Bgra[offset + 1];
                        red += image.Bgra[offset + 2];
                        count++;
                    }
                }

                int pixel = ((dy * SignLayer.ImageSize) + dx) * SignLayer.Channels;
                double samples = count * 255.0;
                inputs[pixel] = red / samples;
                inputs[pixel + 1] = green / samples;
                inputs[pixel + 2] = blue / samples;
            }
        }
    }
}

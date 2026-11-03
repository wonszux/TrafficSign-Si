using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using TrafficSign_Trainer.src.trainer;

public class Program
{
    private const double LearningRate = 0.05;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".ppm", ".pgm"
    };

    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--batch")
        {
            return RunBatch(args);
        }

        if (args.Length == 2 && args[0] == "--names")
        {
            return WriteNames(args[1]);
        }

        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: TrafficSign-Trainer <plik_wag.json> <folder_zdjec>");
            Console.Error.WriteLine("       TrafficSign-Trainer --batch <folder_klas> <folder_wag> <sekundy> <watki>");
            return 1;
        }

        try
        {
            string weightsPath = Path.GetFullPath(args[0]);
            string imagesDirectory = Path.GetFullPath(args[1]);
            NeuralLayer layer = LoadOrCreate(weightsPath);
            TrainingSet trainingSet = SampleLoader.Load(imagesDirectory, ImageExtensions);

            int epochCount = Math.Min(trainingSet.PositiveCount, trainingSet.NegativeCount);
            Console.WriteLine($"Photo directory: {imagesDirectory}");
            Console.WriteLine($"Positive images: {trainingSet.PositiveCount}");
            Console.WriteLine($"Negative images: {trainingSet.NegativeCount}");
            Console.WriteLine($"Each epoch uses {epochCount} positives and {epochCount} negatives.");
            Console.WriteLine("Press any key or Ctrl+C to stop and save.");

            TrainUntilStopped(layer, trainingSet.Samples);
            Save(weightsPath, layer.Weights, Path.GetFileNameWithoutExtension(weightsPath));
            Console.WriteLine($"Saved weights: {weightsPath}");
            return 0;
        }
        catch (JsonException)
        {
            Console.Error.WriteLine(
                $"Invalid JSON file with weights. Expected an array of {NeuralLayer.WeightCount} finite numbers.");
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            Console.Error.WriteLine($"Error reading input data: {exception.Message}");
        }

        return 1;
    }

    private static int RunBatch(string[] args)
    {
        if (args.Length < 5
            || !int.TryParse(args[3], out int seconds)
            || seconds <= 0
            || !int.TryParse(args[4], out int workers)
            || workers <= 0)
        {
            Console.Error.WriteLine("Usage: TrafficSign-Trainer --batch <folder_klas> <folder_wag> <sekundy> <watki> [klasa...]");
            return 1;
        }

        try
        {
            string imagesRoot = Path.GetFullPath(args[1]);
            string weightsDirectory = Path.GetFullPath(args[2]);
            Console.WriteLine($"Loading class folders from {imagesRoot}");
            ClassImages[] classes = SampleLoader.LoadClasses(imagesRoot, ImageExtensions);
            var selected = new HashSet<string>(args.Skip(5), StringComparer.OrdinalIgnoreCase);
            if (selected.Count > 0)
            {
                string[] missing = selected
                    .Where(name => !classes.Any(classImages => classImages.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    .ToArray();
                if (missing.Length > 0)
                {
                    Console.Error.WriteLine($"Unknown class: {string.Join(", ", missing)}");
                    return 1;
                }
            }

            int imageCount = classes.Sum(classImages => classImages.Inputs.Length);
            double[][] backgrounds = SampleLoader.LoadInputs(Path.Combine(imagesRoot, "background"), ImageExtensions);
            Console.WriteLine($"Loaded {classes.Length} classes, {imageCount} images, {backgrounds.Length} background images.");

            var negativesByClass = new Dictionary<string, double[][]>(classes.Length, StringComparer.Ordinal);
            foreach (ClassImages positive in classes)
            {
                var negatives = new List<double[]>();
                foreach (ClassImages other in classes)
                {
                    if (!string.Equals(other.Name, positive.Name, StringComparison.Ordinal))
                    {
                        negatives.AddRange(other.Inputs);
                    }
                }

                negativesByClass[positive.Name] = negatives.ToArray();
            }

            Directory.CreateDirectory(weightsDirectory);
            ClassImages[] queueItems = selected.Count == 0
                ? classes
                : classes.Where(classImages => selected.Contains(classImages.Name)).ToArray();
            var queue = new ConcurrentQueue<ClassImages>(queueItems);
            int workerCount = Math.Min(workers, queueItems.Length);
            var threads = new Thread[workerCount];
            var consoleLock = new object();
            int failed = 0;
            Console.WriteLine($"Starting {workerCount} threads, {seconds} seconds per sign.");

            for (int index = 0; index < workerCount; index++)
            {
                int threadNumber = index + 1;
                threads[index] = new Thread(() =>
                {
                    while (queue.TryDequeue(out ClassImages? positive))
                    {
                        try
                        {
                            TrainClass(
                                positive,
                                negativesByClass[positive.Name],
                                backgrounds,
                                weightsDirectory,
                                seconds,
                                threadNumber,
                                consoleLock);
                        }
                        catch (Exception exception)
                        {
                            Interlocked.Increment(ref failed);
                            lock (consoleLock)
                            {
                                Console.Error.WriteLine($"[thread {threadNumber}] {positive.Name} failed: {exception.Message}");
                            }
                        }
                    }
                })
                {
                    Name = $"trainer-{threadNumber}",
                    IsBackground = false
                };
                threads[index].Start();
            }

            foreach (Thread thread in threads)
            {
                thread.Join();
            }

            Console.WriteLine(failed == 0
                ? $"Batch finished. Weights saved in {weightsDirectory}"
                : $"Batch finished with {failed} failed class(es). Weights saved in {weightsDirectory}");
            return failed == 0 ? 0 : 1;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            Console.Error.WriteLine($"Error reading input data: {exception.Message}");
            return 1;
        }
    }

    private static void TrainClass(
        ClassImages positive,
        double[][] negatives,
        double[][] backgrounds,
        string weightsDirectory,
        int seconds,
        int threadNumber,
        object consoleLock)
    {
        string weightsPath = Path.Combine(weightsDirectory, $"{positive.Name}.json");
        NeuralLayer layer = OpenOrCreate(weightsPath, positive.Name, threadNumber, consoleLock);
        int count = Math.Min(positive.Inputs.Length, negatives.Length);
        var batch = new TrainingSample[count * 2];
        long deadline = Environment.TickCount64 + (seconds * 1000L);
        int epochs = 0;
        double meanAbsoluteError = 1;
        while (Environment.TickCount64 < deadline)
        {
            epochs++;
            FillBalanced(batch, positive.Inputs, negatives, backgrounds);
            Random.Shared.Shuffle(batch);
            double absoluteError = 0;
            int processed = 0;
            foreach (TrainingSample sample in batch)
            {
                if (Environment.TickCount64 >= deadline)
                {
                    break;
                }

                processed++;
                absoluteError += Math.Abs(sample.Target - layer.Forward(sample.Inputs));
                layer.Learn(sample.Inputs, sample.Target, LearningRate);
            }

            if (processed > 0)
            {
                meanAbsoluteError = absoluteError / processed;
            }
        }

        Save(weightsPath, layer.Weights, positive.Name);
        lock (consoleLock)
        {
            Console.WriteLine(
                $"[thread {threadNumber}] saved {positive.Name} epochs={epochs} mean absolute error {meanAbsoluteError.ToString("F4", CultureInfo.InvariantCulture)}");
        }
    }

    private static void FillBalanced(
        TrainingSample[] batch,
        double[][] positives,
        double[][] negatives,
        double[][] backgrounds)
    {
        int count = batch.Length / 2;
        CopyInputs(positives, batch, 0, count, 1.0);
        if (backgrounds.Length == 0 || negatives.Length == 0)
        {
            CopyInputs(backgrounds.Length == 0 ? negatives : backgrounds, batch, count, count, 0.0);
            return;
        }

        int backgroundCount = Math.Min(backgrounds.Length, (count + 1) / 2);
        int signCount = count - backgroundCount;
        CopyInputs(negatives, batch, count, signCount, 0.0);
        CopyInputs(backgrounds, batch, count + signCount, backgroundCount, 0.0);
    }

    private static void CopyInputs(double[][] source, TrainingSample[] destination, int offset, int count, double target)
    {
        if (source.Length == count)
        {
            for (int index = 0; index < count; index++)
            {
                destination[offset + index] = new TrainingSample(source[index], target);
            }

            return;
        }

        var order = new int[source.Length];
        for (int index = 0; index < order.Length; index++)
        {
            order[index] = index;
        }

        for (int index = 0; index < count; index++)
        {
            int swap = Random.Shared.Next(index, source.Length);
            (order[index], order[swap]) = (order[swap], order[index]);
            destination[offset + index] = new TrainingSample(source[order[index]], target);
        }
    }

    private static NeuralLayer OpenOrCreate(string weightsPath, string name, int threadNumber, object consoleLock)
    {
        if (File.Exists(weightsPath))
        {
            try
            {
                double[] weights = ReadWeights(File.ReadAllText(weightsPath));
                NeuralLayer continued = new NeuralLayer(weights);
                lock (consoleLock)
                {
                    Console.WriteLine($"[thread {threadNumber}] continue {name} ({continued.Weights.Length} weights)");
                }

                return continued;
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException)
            {
                lock (consoleLock)
                {
                    Console.WriteLine($"[thread {threadNumber}] {name} has weights of a different size, starting new");
                }
            }
        }
        else
        {
            lock (consoleLock)
            {
                Console.WriteLine($"[thread {threadNumber}] new {name}");
            }
        }

        return NeuralLayer.CreateRandom();
    }

    private static NeuralLayer LoadOrCreate(string weightsPath)
    {
        if (!File.Exists(weightsPath))
        {
            NeuralLayer created = NeuralLayer.CreateRandom();
            Save(weightsPath, created.Weights, Path.GetFileNameWithoutExtension(weightsPath));
            Console.WriteLine($"Created weights: {weightsPath}");
            return created;
        }

        double[] weights = ReadWeights(File.ReadAllText(weightsPath));
        Console.WriteLine($"Loaded weights: {weightsPath}");
        double meanSquare = weights.Average(weight => weight * weight);
        if (meanSquare > NeuralLayer.InitialWeightScale * NeuralLayer.InitialWeightScale * 64)
        {
            Console.Error.WriteLine(
                "These weights are large enough that the sigmoid is stuck near 0 or 1. Delete the JSON file to start from small random weights.");
        }

        return new NeuralLayer(weights);
    }

    private static void TrainUntilStopped(NeuralLayer layer, TrainingSample[] samples)
    {
        using var stop = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            stop.Cancel();
        };
        Console.CancelKeyPress += cancel;

        try
        {
            int epoch = 0;
            while (!StopRequested(stop.Token))
            {
                epoch++;
                TrainingSample[] batch = Balance(samples);
                Random.Shared.Shuffle(batch);
                double absoluteError = 0;
                int processed = 0;
                foreach (TrainingSample sample in batch)
                {
                    if (StopRequested(stop.Token))
                    {
                        break;
                    }

                    processed++;
                    absoluteError += Math.Abs(sample.Target - layer.Forward(sample.Inputs));
                    layer.Learn(sample.Inputs, sample.Target, LearningRate);
                }

                if (processed == 0)
                {
                    break;
                }

                double meanAbsoluteError = absoluteError / processed;
                Console.WriteLine(
                    $"Epoch {epoch}: mean absolute error {meanAbsoluteError.ToString("F4", CultureInfo.InvariantCulture)}");
                Console.Out.Flush();
            }

            try
            {
                if (!Console.IsInputRedirected && Console.KeyAvailable)
                {
                    Console.ReadKey(intercept: true);
                }
            }
            catch (InvalidOperationException)
            {
            }
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
        }
    }

    private static TrainingSample[] Balance(TrainingSample[] samples)
    {
        var positives = new List<TrainingSample>();
        var negatives = new List<TrainingSample>();
        foreach (TrainingSample sample in samples)
        {
            if (sample.Target >= 0.5)
            {
                positives.Add(sample);
            }
            else
            {
                negatives.Add(sample);
            }
        }

        int count = Math.Min(positives.Count, negatives.Count);
        var batch = new TrainingSample[count * 2];
        CopySample(positives, batch, 0, count);
        CopySample(negatives, batch, count, count);
        return batch;
    }

    private static void CopySample(List<TrainingSample> source, TrainingSample[] destination, int offset, int count)
    {
        if (source.Count == count)
        {
            for (int index = 0; index < count; index++)
            {
                destination[offset + index] = source[index];
            }

            return;
        }

        var order = new int[source.Count];
        for (int index = 0; index < order.Length; index++)
        {
            order[index] = index;
        }

        for (int index = 0; index < count; index++)
        {
            int swap = Random.Shared.Next(index, source.Count);
            (order[index], order[swap]) = (order[swap], order[index]);
            destination[offset + index] = source[order[index]];
        }
    }

    private static bool StopRequested(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return true;
        }

        if (Console.IsInputRedirected)
        {
            return false;
        }

        try
        {
            return Console.KeyAvailable;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static int WriteNames(string weightsDirectory)
    {
        foreach (string path in Directory.EnumerateFiles(weightsDirectory, "*.json"))
        {
            string className = Path.GetFileNameWithoutExtension(path);
            Save(path, ReadWeights(File.ReadAllText(path)), className);
            Console.WriteLine($"{className} -> {SignNames.Display(className)}");
        }

        return 0;
    }

    private static double[] ReadWeights(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        JsonElement array = root.ValueKind == JsonValueKind.Array
            ? root
            : root.GetProperty("weights");
        return array.Deserialize<double[]>() ?? throw new JsonException("Weights cannot be null.");
    }

    private static void Save(string weightsPath, double[] weights, string className)
    {
        string? directory = Path.GetDirectoryName(weightsPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var document = new WeightDocument(SignNames.Display(className), weights);
        File.WriteAllText(weightsPath, JsonSerializer.Serialize(document, JsonOptions));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed record WeightDocument(string Name, double[] Weights);
}

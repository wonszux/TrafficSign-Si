using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace TrafficSign_Detector;

public sealed partial class MainPage : Page
{
    private readonly ObservableCollection<NeuronItem> _neurons = [];
    private double[]? _inputs;

    public MainPage()
    {
        InitializeComponent();
        NeuronList.ItemsSource = _neurons;
    }

    private async void PickFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.Desktop
        };
        picker.FileTypeFilter.Add("*");
        InitializePicker(picker);

        StorageFolder? folder = await picker.PickSingleFolderAsync();
        if (folder is null)
        {
            return;
        }

        await LoadFolderAsync(folder);
    }

    private async void PickImage_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary
        };
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".bmp");
        picker.FileTypeFilter.Add(".gif");
        picker.FileTypeFilter.Add(".tif");
        picker.FileTypeFilter.Add(".tiff");
        picker.FileTypeFilter.Add(".webp");
        InitializePicker(picker);

        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            _inputs = await ImageEncoder.EncodeAsync(file);
            using var stream = await file.OpenReadAsync();
            var preview = new BitmapImage();
            await preview.SetSourceAsync(stream);
            PreviewImage.Source = preview;
            HideStatus();
            UpdateAnswer();
        }
        catch (Exception exception)
        {
            _inputs = null;
            PreviewImage.Source = null;
            ShowStatus($"Cannot read this image: {exception.Message}", InfoBarSeverity.Error);
            UpdateAnswer();
        }
    }

    private void EnableAll_Click(object sender, RoutedEventArgs e)
    {
        SetAllActive(true);
    }

    private void DisableAll_Click(object sender, RoutedEventArgs e)
    {
        SetAllActive(false);
    }

    private async Task LoadFolderAsync(StorageFolder folder)
    {
        foreach (NeuronItem previous in _neurons)
        {
            previous.ActivityChanged -= Neuron_ActivityChanged;
        }

        _neurons.Clear();
        FolderPathText.Text = string.IsNullOrEmpty(folder.Path) ? folder.Name : folder.Path;

        IReadOnlyList<StorageFile> files = await folder.GetFilesAsync();
        int skipped = 0;
        foreach (StorageFile file in files.OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!file.FileType.Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                string json = await FileIO.ReadTextAsync(file);
                var neuron = new NeuronItem(Path.GetFileNameWithoutExtension(file.Name), SignLayer.FromJson(json));
                neuron.ActivityChanged += Neuron_ActivityChanged;
                _neurons.Add(neuron);
            }
            catch (Exception)
            {
                skipped++;
            }
        }

        if (_neurons.Count == 0)
        {
            ShowStatus($"This folder has no weight files of {SignLayer.WeightCount} numbers.", InfoBarSeverity.Warning);
        }
        else if (skipped > 0)
        {
            ShowStatus($"Skipped {skipped} JSON file{(skipped == 1 ? "" : "s")} that are not {SignLayer.WeightCount}-weight neurons.", InfoBarSeverity.Warning);
        }
        else
        {
            HideStatus();
        }

        UpdateAnswer();
    }

    private void Neuron_ActivityChanged(object? sender, EventArgs e)
    {
        UpdateAnswer();
    }

    private void SetAllActive(bool active)
    {
        foreach (NeuronItem neuron in _neurons)
        {
            neuron.IsActive = active;
        }

        UpdateAnswer();
    }

    private void UpdateAnswer()
    {
        if (_inputs is null)
        {
            foreach (NeuronItem neuron in _neurons)
            {
                neuron.Score = null;
            }

            AnswerText.Text = _neurons.Count == 0
                ? "Choose a weights folder and an image."
                : "Choose an image.";
            return;
        }

        NeuronItem? best = null;
        foreach (NeuronItem neuron in _neurons)
        {
            double score = neuron.Layer.Forward(_inputs);
            neuron.Score = score;
            if (!neuron.IsActive)
            {
                continue;
            }

            if (best is null || score > best.Score)
            {
                best = neuron;
            }
        }

        if (best is null)
        {
            AnswerText.Text = "Turn on at least one neuron.";
            return;
        }

        AnswerText.Text = $"{best.Name}{Environment.NewLine}{best.ScoreText}";
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }

    private void HideStatus()
    {
        StatusBar.IsOpen = false;
    }

    private static void InitializePicker(object picker)
    {
        Window? window = App.Window;
        if (window is null)
        {
            return;
        }

        IntPtr handle = WindowNative.GetWindowHandle(window);
        InitializeWithWindow.Initialize(picker, handle);
    }
}

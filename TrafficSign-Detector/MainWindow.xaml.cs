using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Media.Imaging;
using TrafficSign.Core;
using Windows.Storage.Pickers;

namespace TrafficSign.Detector;

public sealed partial class MainWindow : Window
{
    private string[]? _labels;
    private NeuralNetwork? _network;
    private double[]? _imageInputs;
    private bool _isBusy;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(920, 900));
    }

    private async void ChooseModelAsync(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        SetBusy(true);
        try
        {
            var file = await CreatePicker(".json").PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            var loaded = await Task.Run(() =>
            {
                var model = ModelFile.Load(file.Path);
                return (model.Labels, Network: new NeuralNetwork(model));
            });
            _labels = loaded.Labels;
            _network = loaded.Network;
            ModelDescription.Text = $"{file.Name} · {_labels.Length} klas\n{file.Path}";
            ClearResult();
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ChooseImageAsync(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        SetBusy(true);
        try
        {
            var file = await CreatePicker(".jpg", ".jpeg", ".png").PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            var input = await Task.Run(() => ImagePreprocessor.Process(file.Path));
            var preview = new BitmapImage();
            using (var stream = await file.OpenReadAsync())
            {
                await preview.SetSourceAsync(stream);
            }
            // Replace the previous selection only after decoding both the input and preview.
            _imageInputs = input;
            PreviewImage.Source = preview;
            PreviewPlaceholder.Visibility = Visibility.Collapsed;
            ImageDescription.Text = file.Path;
            ClearResult();
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void PredictAsync(object sender, RoutedEventArgs e)
    {
        if (_isBusy || _labels is null || _network is null || _imageInputs is null)
        {
            return;
        }

        SetBusy(true);
        try
        {
            var labels = _labels;
            var network = _network;
            var input = _imageInputs;
            var scores = await Task.Run(() => network.Predict(input));
            int predictedIndex = Array.IndexOf(scores, scores.Max());
            ResultLabel.Text = labels[predictedIndex];
            ResultScore.Text = $"Wynik softmax: {scores[predictedIndex]:P1}";
            var peer = FrameworkElementAutomationPeer.FromElement(ResultLabel)
                ?? FrameworkElementAutomationPeer.CreatePeerForElement(ResultLabel);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
        catch (Exception exception)
        {
            ClearResult();
            ShowError(exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private FileOpenPicker CreatePicker(params string[] extensions)
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        foreach (var extension in extensions)
        {
            picker.FileTypeFilter.Add(extension);
        }

        return picker;
    }

    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        ChooseModelButton.IsEnabled = !isBusy;
        ChooseImageButton.IsEnabled = !isBusy;
        PredictButton.IsEnabled = !isBusy && _network is not null && _imageInputs is not null;
        BusyIndicator.IsActive = isBusy;
        BusyIndicator.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        if (isBusy)
        {
            ErrorMessage.IsOpen = false;
        }
    }

    private void ClearResult()
    {
        ResultLabel.Text = "Wynik pojawi się tutaj.";
        ResultScore.Text = string.Empty;
    }

    private void ShowError(Exception exception)
    {
        Trace.TraceError(exception.ToString());
        ErrorMessage.Message = exception.Message;
        ErrorMessage.IsOpen = true;
    }
}

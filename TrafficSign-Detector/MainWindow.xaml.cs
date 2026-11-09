using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Media.Imaging;
using TrafficSign.Core;
using Windows.Storage.Pickers;

namespace TrafficSign.Detector;

public sealed partial class MainWindow : Window
{
    private ModelData? _model;
    private NeuralNetwork? _network;
    private double[]? _input;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(920, 900));
    }

    private async void ChooseModel_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            var file = await CreatePicker(".json").PickSingleFileAsync();
            if (file is null) return;

            var loaded = await Task.Run(() =>
            {
                var model = ModelFile.Load(file.Path);
                return (Model: model, Network: new NeuralNetwork(model));
            });
            _model = loaded.Model;
            _network = loaded.Network;
            ModelDescription.Text = $"{file.Name} · {_model.Labels.Length} klas\n{file.Path}";
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

    private async void ChooseImage_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            var file = await CreatePicker(".jpg", ".jpeg", ".png").PickSingleFileAsync();
            if (file is null) return;

            var input = await Task.Run(() => ImagePreprocessor.Process(file.Path));
            var preview = new BitmapImage();
            using (var stream = await file.OpenReadAsync())
            {
                await preview.SetSourceAsync(stream);
            }
            // Replace the previous selection only after decoding both the input and preview.
            _input = input;
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

    private async void Predict_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _model is null || _network is null || _input is null) return;
        SetBusy(true);
        try
        {
            var model = _model;
            var network = _network;
            var input = _input;
            var scores = await Task.Run(() => network.Predict(input));
            var index = Array.IndexOf(scores, scores.Max());
            ResultLabel.Text = model.Labels[index];
            ResultScore.Text = $"Wynik softmax: {scores[index]:P1}";
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
        foreach (var extension in extensions) picker.FileTypeFilter.Add(extension);
        return picker;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ChooseModelButton.IsEnabled = !busy;
        ChooseImageButton.IsEnabled = !busy;
        PredictButton.IsEnabled = !busy && _network is not null && _input is not null;
        BusyIndicator.IsActive = busy;
        BusyIndicator.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (busy) ErrorMessage.IsOpen = false;
    }

    private void ClearResult()
    {
        ResultLabel.Text = "Wynik pojawi się tutaj.";
        ResultScore.Text = string.Empty;
    }

    private void ShowError(Exception exception)
    {
        ErrorMessage.Message = exception.Message;
        ErrorMessage.IsOpen = true;
    }
}

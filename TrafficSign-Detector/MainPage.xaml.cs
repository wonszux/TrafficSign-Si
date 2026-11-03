using System.Collections.ObjectModel;
using System.Net.Http;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace TrafficSign_Detector;

public sealed partial class MainPage : Page
{
    private readonly ObservableCollection<NeuronItem> _neurons = [];
    private const double RecognitionThreshold = 0.30;

    private DecodedImage? _image;
    private bool _hasScan;
    private NeuronItem? _scanWinner;
    private NeuronHit _scanAnswer;
    private bool _manual;
    private NeuronHit _placedHit;
    private bool _moving;
    private bool _resizing;
    private double _dragOffsetX;
    private double _dragOffsetY;
    private double _resizeStartWidth;
    private double _resizeStartPointerX;
    private double _resizeStartPointerY;
    private int _scanGeneration;

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

        await OpenFileAsync(file);
    }

    private void Paste_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = PasteAsync();
    }

    private async Task PasteAsync()
    {
        try
        {
            DataPackageView view = Clipboard.GetContent();
            await OpenPackageAsync(view);
        }
        catch (Exception exception)
        {
            ShowStatus($"Cannot read this image: {exception.Message}", InfoBarSeverity.Error);
        }
    }

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.Caption = "Score image";
        e.Handled = true;
    }

    private async void Root_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        DragOperationDeferral deferral = e.GetDeferral();
        try
        {
            await OpenPackageAsync(e.DataView);
        }
        catch (Exception exception)
        {
            ShowStatus($"Cannot read this image: {exception.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async Task OpenPackageAsync(DataPackageView view)
    {
        if (view.Contains(StandardDataFormats.StorageItems))
        {
            IReadOnlyList<IStorageItem> items = await view.GetStorageItemsAsync();
            StorageFile? file = items.OfType<StorageFile>().FirstOrDefault(IsImageFile)
                ?? items.OfType<StorageFile>().FirstOrDefault();
            if (file is not null)
            {
                await OpenFileAsync(file);
                return;
            }
        }

        if (view.Contains(StandardDataFormats.Bitmap))
        {
            RandomAccessStreamReference bitmap = await view.GetBitmapAsync();
            using IRandomAccessStreamWithContentType stream = await bitmap.OpenReadAsync();
            await OpenStreamAsync(stream);
            return;
        }

        if (view.Contains(StandardDataFormats.WebLink))
        {
            await OpenUriAsync(await view.GetWebLinkAsync());
            return;
        }

        if (view.Contains(StandardDataFormats.Uri))
        {
            await OpenUriAsync(await view.GetUriAsync());
            return;
        }

        if (view.Contains(StandardDataFormats.Text))
        {
            string text = await view.GetTextAsync();
            if (TryGetHttpUri(text, out Uri uri))
            {
                await OpenUriAsync(uri);
                return;
            }
        }

        if (view.Contains(StandardDataFormats.Html))
        {
            string html = await view.GetHtmlFormatAsync();
            if (TryGetHtmlImage(html, out Uri uri))
            {
                await OpenUriAsync(uri);
                return;
            }
        }

        ShowStatus("Paste or drop an image file or an image link.", InfoBarSeverity.Warning);
    }

    private async Task OpenFileAsync(StorageFile file)
    {
        using IRandomAccessStream stream = await file.OpenReadAsync();
        await OpenStreamAsync(stream);
    }

    private async Task OpenUriAsync(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            ShowStatus("Only http and https links can be opened.", InfoBarSeverity.Warning);
            return;
        }

        try
        {
            ShowStatus("Reading image…", InfoBarSeverity.Informational);
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TrafficSign-Detector");
            using HttpResponseMessage response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 32 * 1024 * 1024)
            {
                ShowStatus("That image is larger than 32 MB.", InfoBarSeverity.Warning);
                return;
            }

            byte[] bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.Length > 32 * 1024 * 1024)
            {
                ShowStatus("That image is larger than 32 MB.", InfoBarSeverity.Warning);
                return;
            }

            using var memory = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(memory))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                writer.DetachStream();
            }

            memory.Seek(0);
            await OpenStreamAsync(memory);
        }
        catch (Exception exception)
        {
            ShowStatus($"Cannot read this image: {exception.Message}", InfoBarSeverity.Error);
        }
    }

    private async Task OpenStreamAsync(IRandomAccessStream stream)
    {
        try
        {
            stream.Seek(0);
            _image = await ImageEncoder.DecodeAsync(stream);
            ClearRecognition();
            stream.Seek(0);
            var preview = new BitmapImage();
            await preview.SetSourceAsync(stream);
            PreviewImage.Source = preview;
            FitPreviewToPhoto();
            HideStatus();
            await ScoreAsync();
        }
        catch (Exception exception)
        {
            _image = null;
            ClearRecognition();
            PreviewImage.Source = null;
            ShowStatus($"Cannot read this image: {exception.Message}", InfoBarSeverity.Error);
            UpdateAnswer();
        }
    }

    private static bool IsImageFile(StorageFile file)
    {
        return HasImageExtension(file.FileType);
    }

    private static bool HasImageExtension(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".gif", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".tif", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetHttpUri(string text, out Uri uri)
    {
        text = text.Trim().Trim('"');
        if (Uri.TryCreate(text, UriKind.Absolute, out Uri? parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }

    private static bool TryGetHtmlImage(string html, out Uri uri)
    {
        uri = null!;
        Uri? fallback = null;
        int search = 0;
        while (search < html.Length)
        {
            int src = html.IndexOf("src=", search, StringComparison.OrdinalIgnoreCase);
            if (src < 0)
            {
                break;
            }

            int start = src + 4;
            if (start >= html.Length || (html[start] != '"' && html[start] != '\''))
            {
                search = start;
                continue;
            }

            char quote = html[start];
            start++;
            int end = html.IndexOf(quote, start);
            if (end <= start)
            {
                break;
            }

            string value = System.Net.WebUtility.HtmlDecode(html[start..end]);
            if (TryGetHttpUri(value, out Uri found))
            {
                if (HasImageExtension(found.AbsolutePath))
                {
                    uri = found;
                    return true;
                }

                fallback ??= found;
            }

            search = end + 1;
        }

        if (fallback is null)
        {
            return false;
        }

        uri = fallback;
        return true;
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
                SignLayer layer = SignLayer.FromJson(json);
                string fileName = Path.GetFileNameWithoutExtension(file.Name);
                var neuron = new NeuronItem(layer.DisplayName ?? fileName, layer);
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

        await ScoreAsync();
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

    private async Task ScoreAsync()
    {
        if (_image is null || _neurons.Count == 0)
        {
            ClearRecognition();
            UpdateAnswer();
            return;
        }

        int generation = ++_scanGeneration;
        AnswerText.Text = "Scanning squares…";
        HitRectangle.Visibility = Visibility.Collapsed;
        ResizeHandle.Visibility = Visibility.Collapsed;
        NeuronItem[] neurons = _neurons.ToArray();
        SignLayer[] layers = neurons.Select(neuron => neuron.Layer).ToArray();
        DecodedImage image = _image;
        ScanResult scan = await Task.Run(() => ImageScanner.Scan(image, layers));
        if (generation != _scanGeneration)
        {
            return;
        }

        for (int index = 0; index < neurons.Length; index++)
        {
            neurons[index].PlacedScore = null;
            neurons[index].ScanHit = scan.Hits[index];
        }

        _hasScan = true;
        _manual = false;
        _scanWinner = scan.AnswerNeuron >= 0 ? neurons[scan.AnswerNeuron] : null;
        _scanAnswer = scan.Answer;
        UpdateAnswer();
    }

    private void UpdateAnswer()
    {
        if (!_hasScan || _image is null)
        {
            foreach (NeuronItem neuron in _neurons)
            {
                neuron.Score = null;
            }

            HitRectangle.Visibility = Visibility.Collapsed;
            ResizeHandle.Visibility = Visibility.Collapsed;
            DragHint.Visibility = Visibility.Collapsed;
            AnswerText.Text = _neurons.Count == 0
                ? "Choose a weights folder and an image."
                : "Choose, paste, or drop an image.";
            return;
        }

        foreach (NeuronItem neuron in _neurons)
        {
            neuron.Score = _manual ? neuron.PlacedScore ?? 0 : neuron.ScanHit.Score;
        }

        SortByScore();
        if (_moving || _resizing)
        {
            return;
        }

        if (!TryChooseWinner(out NeuronItem? best, out NeuronHit bestHit) || best is null)
        {
            HitRectangle.Visibility = Visibility.Collapsed;
            ResizeHandle.Visibility = Visibility.Collapsed;
            DragHint.Visibility = Visibility.Collapsed;
            AnswerText.Text = "Turn on at least one neuron.";
            return;
        }

        string headline = bestHit.Score < RecognitionThreshold
            ? "Not recognised"
            : best.Name;
        AnswerText.Text = $"{headline}{Environment.NewLine}{bestHit.Score.ToString("P1")}{Environment.NewLine}{bestHit.Side}×{bestHit.Side} square";
        DragHint.Visibility = Visibility.Visible;
        PlaceHighlight(bestHit);
    }

    private void SortByScore()
    {
        List<NeuronItem> ordered = _neurons
            .OrderByDescending(neuron => neuron.Score ?? -1)
            .ThenBy(neuron => neuron.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        for (int target = 0; target < ordered.Count; target++)
        {
            int current = _neurons.IndexOf(ordered[target]);
            if (current != target)
            {
                _neurons.Move(current, target);
            }
        }
    }

    private void PreviewHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        FitPreviewToPhoto();
        if (_moving || _resizing)
        {
            return;
        }

        if (TryChooseWinner(out _, out NeuronHit winner))
        {
            PlaceHighlight(winner);
        }
    }

    private void FitPreviewToPhoto()
    {
        if (_image is null || PreviewHost.ActualWidth <= 0 || PreviewHost.ActualHeight <= 0)
        {
            return;
        }

        double scale = Math.Min(PreviewHost.ActualWidth / _image.Width, PreviewHost.ActualHeight / _image.Height);
        double width = Math.Max(1, _image.Width * scale);
        double height = Math.Max(1, _image.Height * scale);
        PreviewImage.Width = width;
        PreviewImage.Height = height;
        HitCanvas.Width = width;
        HitCanvas.Height = height;
    }

    private bool TryChooseWinner(out NeuronItem? neuron, out NeuronHit hit)
    {
        neuron = null;
        hit = default;
        if (!_hasScan)
        {
            return false;
        }

        if (_manual)
        {
            bool placed = false;
            foreach (NeuronItem candidate in _neurons)
            {
                if (!candidate.IsActive)
                {
                    continue;
                }

                double score = candidate.PlacedScore ?? 0;
                if (!placed || score > hit.Score)
                {
                    neuron = candidate;
                    hit = _placedHit with { Score = score };
                    placed = true;
                }
            }

            return placed;
        }

        if (_scanWinner is not null && _scanWinner.IsActive)
        {
            neuron = _scanWinner;
            hit = _scanAnswer;
            return true;
        }

        bool found = false;
        foreach (NeuronItem candidate in _neurons)
        {
            if (!candidate.IsActive)
            {
                continue;
            }

            if (!found || candidate.ScanHit.Score > hit.Score)
            {
                neuron = candidate;
                hit = candidate.ScanHit;
                found = true;
            }
        }

        return found;
    }

    private void ClearRecognition()
    {
        _hasScan = false;
        _manual = false;
        _scanWinner = null;
        foreach (NeuronItem neuron in _neurons)
        {
            neuron.PlacedScore = null;
        }
    }

    private void PlaceHighlight(NeuronHit hit)
    {
        if (_image is null)
        {
            HitRectangle.Visibility = Visibility.Collapsed;
            ResizeHandle.Visibility = Visibility.Collapsed;
            return;
        }

        if (!TryMap(out double scale, out _, out _))
        {
            HitRectangle.Visibility = Visibility.Collapsed;
            ResizeHandle.Visibility = Visibility.Collapsed;
            return;
        }

        Canvas.SetLeft(HitRectangle, hit.X * scale);
        Canvas.SetTop(HitRectangle, hit.Y * scale);
        double drawn = Math.Max(1, hit.Side * scale);
        HitRectangle.Width = drawn;
        HitRectangle.Height = drawn;
        HitRectangle.Visibility = Visibility.Visible;
        MoveResizeHandle();
    }

    private void MoveResizeHandle()
    {
        Canvas.SetLeft(ResizeHandle, Canvas.GetLeft(HitRectangle) + HitRectangle.Width - (ResizeHandle.Width / 2));
        Canvas.SetTop(ResizeHandle, Canvas.GetTop(HitRectangle) + HitRectangle.Height - (ResizeHandle.Height / 2));
        ResizeHandle.Visibility = HitRectangle.Visibility;
    }

    private void HitRectangle_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeAll);
    }

    private void HitRectangle_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_moving && !_resizing)
        {
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
        }
    }

    private void HitRectangle_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_image is null || !_hasScan)
        {
            return;
        }

        var position = e.GetCurrentPoint(HitCanvas).Position;
        _dragOffsetX = position.X - Canvas.GetLeft(HitRectangle);
        _dragOffsetY = position.Y - Canvas.GetTop(HitRectangle);
        _moving = true;
        HitRectangle.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void HitRectangle_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_moving)
        {
            return;
        }

        if (!TryMap(out _, out _, out _) || _image is null)
        {
            return;
        }

        var position = e.GetCurrentPoint(HitCanvas).Position;
        double left = position.X - _dragOffsetX;
        double top = position.Y - _dragOffsetY;
        double width = HitRectangle.Width;
        double height = HitRectangle.Height;
        left = Math.Clamp(left, 0, Math.Max(0, HitCanvas.Width - width));
        top = Math.Clamp(top, 0, Math.Max(0, HitCanvas.Height - height));
        Canvas.SetLeft(HitRectangle, left);
        Canvas.SetTop(HitRectangle, top);
        MoveResizeHandle();
        e.Handled = true;
    }

    private void HitRectangle_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        EndDrag(e);
    }

    private void HitRectangle_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        bool commit = _moving || _resizing;
        _moving = false;
        _resizing = false;
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
        if (commit)
        {
            CommitVisualSquare();
        }
    }

    private void EndDrag(PointerRoutedEventArgs e)
    {
        if (!_moving)
        {
            return;
        }

        _moving = false;
        HitRectangle.ReleasePointerCapture(e.Pointer);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
        CommitVisualSquare();
    }

    private void HitRectangle_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (_image is null || HitCanvas.Width <= 0 || HitCanvas.Height <= 0)
        {
            return;
        }

        double side = Math.Min(HitCanvas.Width, HitCanvas.Height);
        Canvas.SetLeft(HitRectangle, (HitCanvas.Width - side) / 2);
        Canvas.SetTop(HitRectangle, (HitCanvas.Height - side) / 2);
        HitRectangle.Width = side;
        HitRectangle.Height = side;
        MoveResizeHandle();
        CommitVisualSquare();
        e.Handled = true;
    }

    private void HitCanvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (_image is null || !_hasScan || _moving || _resizing || HitRectangle.Visibility != Visibility.Visible)
        {
            return;
        }

        if (!TryMap(out double scale, out _, out _))
        {
            return;
        }

        int delta = e.GetCurrentPoint(HitCanvas).Properties.MouseWheelDelta;
        if (delta == 0)
        {
            return;
        }

        double factor = delta > 0 ? 1.12 : 1 / 1.12;
        double minimum = Math.Min(SignLayer.ImageSize * scale, Math.Min(HitCanvas.Width, HitCanvas.Height));
        double maximum = Math.Min(HitCanvas.Width, HitCanvas.Height);
        double side = Math.Clamp(HitRectangle.Width * factor, minimum, maximum);
        double centerX = Canvas.GetLeft(HitRectangle) + (HitRectangle.Width / 2);
        double centerY = Canvas.GetTop(HitRectangle) + (HitRectangle.Height / 2);
        double left = centerX - (side / 2);
        double top = centerY - (side / 2);
        if (side >= maximum - 0.5)
        {
            left = (HitCanvas.Width - side) / 2;
            top = (HitCanvas.Height - side) / 2;
        }

        left = Math.Clamp(left, 0, Math.Max(0, HitCanvas.Width - side));
        top = Math.Clamp(top, 0, Math.Max(0, HitCanvas.Height - side));
        Canvas.SetLeft(HitRectangle, left);
        Canvas.SetTop(HitRectangle, top);
        HitRectangle.Width = side;
        HitRectangle.Height = side;
        MoveResizeHandle();
        CommitVisualSquare();
        e.Handled = true;
    }

    private void ResizeHandle_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthwestSoutheast);
    }

    private void ResizeHandle_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var position = e.GetCurrentPoint(HitCanvas).Position;
        _resizeStartWidth = HitRectangle.Width;
        _resizeStartPointerX = position.X;
        _resizeStartPointerY = position.Y;
        _resizing = true;
        ResizeHandle.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void ResizeHandle_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_resizing || _image is null || !TryMap(out double scale, out _, out _))
        {
            return;
        }

        var position = e.GetCurrentPoint(HitCanvas).Position;
        double delta = ((position.X - _resizeStartPointerX) + (position.Y - _resizeStartPointerY)) / 2;
        double minimum = Math.Min(SignLayer.ImageSize * scale, Math.Min(HitCanvas.Width, HitCanvas.Height));
        double maximum = Math.Min(HitCanvas.Width, HitCanvas.Height);
        double side = Math.Clamp(_resizeStartWidth + delta, minimum, maximum);
        double left = Canvas.GetLeft(HitRectangle);
        double top = Canvas.GetTop(HitRectangle);
        if (side >= maximum - 0.5)
        {
            left = (HitCanvas.Width - side) / 2;
            top = (HitCanvas.Height - side) / 2;
        }
        else
        {
            if (left + side > HitCanvas.Width)
            {
                left = HitCanvas.Width - side;
            }

            if (top + side > HitCanvas.Height)
            {
                top = HitCanvas.Height - side;
            }

            left = Math.Max(0, left);
            top = Math.Max(0, top);
        }

        Canvas.SetLeft(HitRectangle, left);
        Canvas.SetTop(HitRectangle, top);
        HitRectangle.Width = side;
        HitRectangle.Height = side;
        MoveResizeHandle();
        e.Handled = true;
    }

    private void ResizeHandle_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_resizing)
        {
            return;
        }

        _resizing = false;
        ResizeHandle.ReleasePointerCapture(e.Pointer);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
        CommitVisualSquare();
    }

    private void CommitVisualSquare()
    {
        if (_image is null || _neurons.Count == 0 || !TryMap(out double scale, out _, out _))
        {
            return;
        }

        int limit = Math.Min(_image.Width, _image.Height);
        int side = (int)Math.Round(HitRectangle.Width / scale);
        side = Math.Clamp(side, Math.Min(SignLayer.ImageSize, limit), Math.Max(1, limit));
        int x = (int)Math.Round(Canvas.GetLeft(HitRectangle) / scale);
        int y = (int)Math.Round(Canvas.GetTop(HitRectangle) / scale);
        if (side >= limit - 1)
        {
            side = limit;
            x = (_image.Width - side) / 2;
            y = (_image.Height - side) / 2;
        }

        x = Math.Clamp(x, 0, Math.Max(0, _image.Width - side));
        y = Math.Clamp(y, 0, Math.Max(0, _image.Height - side));
        NeuronItem[] neurons = _neurons.ToArray();
        SignLayer[] layers = neurons.Select(neuron => neuron.Layer).ToArray();
        double[] scores = ImageScanner.ScoreWindow(_image, layers, x, y, side);
        for (int index = 0; index < neurons.Length; index++)
        {
            neurons[index].PlacedScore = scores[index];
        }

        _manual = true;
        _placedHit = new NeuronHit(0, x, y, side);
        UpdateAnswer();
    }

    private bool TryMap(out double scale, out double offsetX, out double offsetY)
    {
        scale = 0;
        offsetX = 0;
        offsetY = 0;
        if (_image is null)
        {
            return false;
        }

        if (HitCanvas.ActualWidth <= 0 || HitCanvas.ActualHeight <= 0 || _image.Width <= 0 || _image.Height <= 0)
        {
            return false;
        }

        scale = HitCanvas.ActualWidth / _image.Width;
        offsetX = 0;
        offsetY = 0;
        return scale > 0;
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

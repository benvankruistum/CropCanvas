using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CropCanvas.Config;
using CropCanvas.Models;
using CropCanvas.Resources;
using CropCanvas.Services;
using CropCanvas.Services.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CropCanvas.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SettingsService _settingsService = new();
    private readonly ScreenService _screenService = new();
    private readonly ImageService _imageService = new();
    private readonly LogService _log = LogService.Instance;
    private readonly ComfyUIService _comfyService = new();
    private readonly StabilityAIService _stabilityService = new();
    private readonly OpenAIService _openAIService = new();
    private readonly HuggingFaceOutpaintService _huggingFaceService = new();
    private AppSettings _settings;

    public ObservableCollection<ImageItem> Images { get; } = [];

    [ObservableProperty]
    private ImageItem? _selectedImage;

    [ObservableProperty]
    private BitmapSource? _displayImage;

    [ObservableProperty]
    private string? _sourceFolderPath;

    [ObservableProperty]
    private string? _outputFolderPath;

    [ObservableProperty]
    private string _imageCountText = "";

    [ObservableProperty]
    private string _croppedCountText = "";

    public string SourceFolderDisplay => string.IsNullOrEmpty(SourceFolderPath)
        ? Strings.SelectSourceFolder : Path.GetFileName(SourceFolderPath) ?? SourceFolderPath;

    public string OutputFolderDisplay => string.IsNullOrEmpty(OutputFolderPath)
        ? Strings.SelectOutputFolder : Path.GetFileName(OutputFolderPath) ?? OutputFolderPath;

    [ObservableProperty]
    private int _aspectRatioWidth;

    [ObservableProperty]
    private int _aspectRatioHeight;

    [ObservableProperty]
    private bool _useCustomAspectRatio;

    [ObservableProperty]
    private double _cropAspectRatio;

    [ObservableProperty]
    private double _normalizedCropX;

    [ObservableProperty]
    private double _normalizedCropY;

    [ObservableProperty]
    private double _normalizedCropWidth;

    [ObservableProperty]
    private double _normalizedCropHeight;

    [ObservableProperty]
    private string _statusText = Strings.StatusSelectFolder;

    [ObservableProperty]
    private string _imageAnalysisText = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isOutpainting;

    [ObservableProperty]
    private bool _hasOutOfBounds;

    [ObservableProperty]
    private bool _isLogVisible;

    [ObservableProperty]
    private bool _useAIPrompt = true;

    [ObservableProperty]
    private string? _openAIApiKey;

    public ObservableCollection<LogEntry> LogEntries => _log.Entries;

    [ObservableProperty]
    private OutputFormat _selectedOutputFormat;

    [ObservableProperty]
    private int _jpegQuality;

    [ObservableProperty]
    private OutpaintProvider _selectedProvider;

    [ObservableProperty]
    private string? _stabilityApiKey;

    [ObservableProperty]
    private string _comfyUIBaseUrl = ComfyUIConfig.DefaultBaseUrl;

    [ObservableProperty]
    private string _selectedLanguage = "nl";

    [ObservableProperty]
    private int _screenResW;

    [ObservableProperty]
    private int _screenResH;

    /// <summary>50–100: AI upload as % of full resolution (100 = no extra downscale).</summary>
    [ObservableProperty]
    private int _aiUploadScalePercent = 100;

    /// <summary>1–100: Comfy/HF steps, Stability creativity; OpenAI ignores.</summary>
    [ObservableProperty]
    private int _aiDetailLevel = 50;

    /// <summary>Optional horizontal guide line on the canvas for aligning the horizon while straightening.</summary>
    [ObservableProperty]
    private bool _showStraightenHorizonGuide;

    /// <summary>Vertical position of the horizon guide (0 = top of image, 1 = bottom), in normalized image space.</summary>
    [ObservableProperty]
    private double _straightenHorizonGuideNormalizedY = 0.5;

    /// <summary>Fine straightening (±3°), baked into the bitmap before crop/AI.</summary>
    private double _rotationDegrees;

    public double RotationDegrees
    {
        get => _rotationDegrees;
        set
        {
            var v = Math.Clamp(value, -ImageConfig.RotationMaxDegrees, ImageConfig.RotationMaxDegrees);
            if (Math.Abs(_rotationDegrees - v) < 1e-9) return;
            _rotationDegrees = v;
            OnPropertyChanged(nameof(RotationDegrees));
            OnPropertyChanged(nameof(EffectiveImageWidth));
            OnPropertyChanged(nameof(EffectiveImageHeight));
            if (SelectedImage != null)
            {
                try
                {
                    DisplayImage = _imageService.LoadDisplayImage(SelectedImage.FilePath, ImageConfig.DisplayWidth, _rotationDegrees);
                }
                catch (Exception ex)
                {
                    StatusText = string.Format(Strings.StatusLoadError, ex.Message);
                }

                ResetCropToDefault();
                UpdateStatusText();
            }
        }
    }

    /// <summary>Full-res width after applying <see cref="RotationDegrees"/>.</summary>
    public int EffectiveImageWidth =>
        SelectedImage == null
            ? 0
            : ImageService.GetRotatedDimensions(SelectedImage.OriginalWidth, SelectedImage.OriginalHeight, _rotationDegrees).Width;

    /// <summary>Full-res height after applying <see cref="RotationDegrees"/>.</summary>
    public int EffectiveImageHeight =>
        SelectedImage == null
            ? 0
            : ImageService.GetRotatedDimensions(SelectedImage.OriginalWidth, SelectedImage.OriginalHeight, _rotationDegrees).Height;

    public string CropButtonText => HasOutOfBounds ? Strings.AICrop : Strings.Crop;

    public MainViewModel()
    {
        _settings = _settingsService.Load();

        // Detect screen
        var (sw, sh) = _screenService.GetScreenResolution();
        ScreenResW = sw;
        ScreenResH = sh;
        var (rw, rh) = _screenService.GetSimplifiedRatio(sw, sh);

        // Apply settings
        SourceFolderPath = _settings.SourceFolderPath;
        OutputFolderPath = _settings.OutputFolderPath;
        UseCustomAspectRatio = _settings.UseCustomAspectRatio;
        AspectRatioWidth = _settings.UseCustomAspectRatio ? _settings.AspectRatioWidth : rw;
        AspectRatioHeight = _settings.UseCustomAspectRatio ? _settings.AspectRatioHeight : rh;
        SelectedOutputFormat = _settings.OutputFormat;
        JpegQuality = _settings.JpegQuality > 0 ? _settings.JpegQuality : ImageConfig.DefaultJpegQuality;
        SelectedProvider = _settings.OutpaintProvider;
        StabilityApiKey = _settings.StabilityApiKey;
        UseAIPrompt = _settings.UseAIPrompt;
        if (!string.IsNullOrEmpty(StabilityApiKey))
            _stabilityService.SetApiKey(StabilityApiKey);
        OpenAIApiKey = _settings.OpenAIApiKey;
        if (!string.IsNullOrEmpty(OpenAIApiKey))
            _openAIService.SetApiKey(OpenAIApiKey);
        ComfyUIBaseUrl = string.IsNullOrWhiteSpace(_settings.ComfyUIBaseUrl)
            ? ComfyUIConfig.DefaultBaseUrl
            : _settings.ComfyUIBaseUrl;
        _comfyService.SetBaseUrl(ComfyUIBaseUrl);

        var scalePct = _settings.AiUploadScalePercent;
        AiUploadScalePercent = scalePct is >= 50 and <= 100 ? scalePct : 100;
        AiDetailLevel = _settings.AiDetailLevel is >= 1 and <= 100 ? _settings.AiDetailLevel : 50;

        // Apply language setting
        _selectedLanguage = _settings.Language ?? "nl";
        Thread.CurrentThread.CurrentUICulture = new CultureInfo(_selectedLanguage);

        UpdateCropAspectRatio();

        // Auto-load source folder if previously set
        if (!string.IsNullOrEmpty(SourceFolderPath) && Directory.Exists(SourceFolderPath))
            _ = LoadImagesAsync(SourceFolderPath);

        _cropSuccessToastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
        _cropSuccessToastTimer.Tick += (_, _) =>
        {
            _cropSuccessToastTimer.Stop();
            IsCropSuccessToastVisible = false;
        };
    }

    private void ShowCropSuccessToast(string title, string detail)
    {
        CropSuccessToastTitle = title;
        CropSuccessToastDetail = detail;
        IsCropSuccessToastVisible = true;
        _cropSuccessToastTimer.Stop();
        _cropSuccessToastTimer.Start();
    }

    partial void OnAspectRatioWidthChanged(int value) => UpdateCropAspectRatio();
    partial void OnAspectRatioHeightChanged(int value) => UpdateCropAspectRatio();

    partial void OnSelectedProviderChanged(OutpaintProvider value)
    {
        OnPropertyChanged(nameof(IsStabilityProvider));
        OnPropertyChanged(nameof(IsOpenAIProvider));
        OnPropertyChanged(nameof(IsComfyProvider));
        OnPropertyChanged(nameof(NeedsApiKey));
        OnPropertyChanged(nameof(HasAIProvider));
        SaveSettings();
    }

    public bool IsStabilityProvider => SelectedProvider == OutpaintProvider.StabilityAI;
    public bool IsOpenAIProvider => SelectedProvider == OutpaintProvider.OpenAI;
    public bool IsComfyProvider => SelectedProvider == OutpaintProvider.ComfyUI;
    public bool NeedsApiKey => IsStabilityProvider || IsOpenAIProvider;
    public bool HasAIProvider => SelectedProvider != OutpaintProvider.Geen;

    partial void OnStabilityApiKeyChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value))
            _stabilityService.SetApiKey(value);
        SaveSettings();
    }

    partial void OnOpenAIApiKeyChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value))
            _openAIService.SetApiKey(value);
        SaveSettings();
    }

    partial void OnComfyUIBaseUrlChanged(string value)
    {
        _comfyService.SetBaseUrl(value);
        SaveSettings();
    }

    partial void OnAiUploadScalePercentChanged(int value) => SaveSettings();

    partial void OnAiDetailLevelChanged(int value) => SaveSettings();

    partial void OnSelectedLanguageChanged(string value)
    {
        Thread.CurrentThread.CurrentUICulture = new CultureInfo(value);
        SaveSettings();
        UpdateStatusText();
        OnPropertyChanged(nameof(SourceFolderDisplay));
        OnPropertyChanged(nameof(OutputFolderDisplay));
        OnPropertyChanged(nameof(CropButtonText));
    }

    partial void OnHasOutOfBoundsChanged(bool value)
    {
        OnPropertyChanged(nameof(CropButtonText));
    }

    partial void OnSourceFolderPathChanged(string? value) => OnPropertyChanged(nameof(SourceFolderDisplay));
    partial void OnOutputFolderPathChanged(string? value) => OnPropertyChanged(nameof(OutputFolderDisplay));

    // Lock icon: locked = screen ratio (not editable), unlocked = custom (editable)
    // \ueae2 = lock, \ueae1 = lock-open
    public string RatioLockIcon => UseCustomAspectRatio ? "\ueae1" : "\ueae2";

    [RelayCommand]
    private void ToggleCustomRatio()
    {
        UseCustomAspectRatio = !UseCustomAspectRatio;
    }

    partial void OnUseCustomAspectRatioChanged(bool value)
    {
        if (!value)
        {
            var (rw, rh) = _screenService.GetSimplifiedRatio(ScreenResW, ScreenResH);
            AspectRatioWidth = rw;
            AspectRatioHeight = rh;
        }
        OnPropertyChanged(nameof(RatioLockIcon));
        SaveSettings();
    }

    private void UpdateCropAspectRatio()
    {
        if (AspectRatioHeight > 0)
            CropAspectRatio = (double)AspectRatioWidth / AspectRatioHeight;
        SaveSettings();
    }

    partial void OnSelectedImageChanged(ImageItem? value)
    {
        if (value == null)
        {
            _rotationDegrees = 0;
            OnPropertyChanged(nameof(RotationDegrees));
            OnPropertyChanged(nameof(EffectiveImageWidth));
            OnPropertyChanged(nameof(EffectiveImageHeight));
            DisplayImage = null;
            StatusText = Strings.StatusSelectImage;
            ShowStraightenHorizonGuide = false;
            StraightenHorizonGuideNormalizedY = 0.5;
            return;
        }

        try
        {
            // DisplayImage must be set FIRST — this triggers ImageAspectRatio
            // in the overlay via code-behind, so the overlay knows the correct
            // image bounds before normalized crop coords are set.
            _log.Info($"Loading: {value.FileName} ({value.OriginalWidth}x{value.OriginalHeight})");
            ShowStraightenHorizonGuide = false;
            StraightenHorizonGuideNormalizedY = 0.5;
            _rotationDegrees = 0;
            OnPropertyChanged(nameof(RotationDegrees));
            OnPropertyChanged(nameof(EffectiveImageWidth));
            OnPropertyChanged(nameof(EffectiveImageHeight));
            DisplayImage = _imageService.LoadDisplayImage(value.FilePath, ImageConfig.DisplayWidth, 0);
            ResetCropToDefault();
            UpdateStatusText();

            // Run image analysis in background
            var filePath = value.FilePath;
            _ = Task.Run(() =>
            {
                try
                {
                    var bytes = _imageService.LoadImageBytes(filePath);
                    var analysis = ImageAnalyzer.Analyze(bytes);
                    var text = $"\u2728 {analysis.TimeOfDay}  |  \ud83c\udf43 {analysis.Season}  |  " +
                               $"\ud83c\udfa8 {analysis.ColorPalette}  |  \ud83d\udca1 {analysis.LightingDirection}  |  " +
                               $"\u2600 {analysis.Mood}  |  " +
                               $"Brightness: {analysis.AverageBrightness:P0}  |  " +
                               $"Warmth: {(analysis.WarmthScore > 0 ? "+" : "")}{analysis.WarmthScore:F2}";
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        ImageAnalysisText = text;
                        _log.Info($"Analysis: {analysis.TimeOfDay}, {analysis.Season}, {analysis.Mood}");
                        _log.Info($"Palette: {analysis.ColorPalette} | Light: {analysis.LightingDirection}");
                        _log.Info($"Brightness: {analysis.AverageBrightness:P0} | Warmth: {analysis.WarmthScore:+0.00;-0.00}");
                    });
                }
                catch
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() => ImageAnalysisText = "");
                }
            });
        }
        catch (Exception ex)
        {
            StatusText = string.Format(Strings.StatusLoadError, ex.Message);
        }
    }

    private void ResetCropToDefault()
    {
        if (SelectedImage == null || CropAspectRatio <= 0) return;

        var (effW, effH) = ImageService.GetRotatedDimensions(
            SelectedImage.OriginalWidth, SelectedImage.OriginalHeight, _rotationDegrees);
        double imageAspect = (double)effW / effH;

        if (Math.Abs(CropAspectRatio - imageAspect) < 0.01)
        {
            // Ratio matches image: select entire image
            NormalizedCropX = 0;
            NormalizedCropY = 0;
            NormalizedCropWidth = 1.0;
            NormalizedCropHeight = 1.0;
        }
        else if (CropAspectRatio > imageAspect)
        {
            // Crop is wider than image: fill width, center vertically
            NormalizedCropWidth = 1.0;
            NormalizedCropHeight = imageAspect / CropAspectRatio;
            NormalizedCropX = 0;
            NormalizedCropY = (1.0 - NormalizedCropHeight) / 2;
        }
        else
        {
            // Crop is taller than image: fill height, center horizontally
            NormalizedCropHeight = 1.0;
            NormalizedCropWidth = CropAspectRatio / imageAspect;
            NormalizedCropX = (1.0 - NormalizedCropWidth) / 2;
            NormalizedCropY = 0;
        }
    }

    private void UpdateStatusText()
    {
        if (SelectedImage == null) return;

        var origW = EffectiveImageWidth;
        var origH = EffectiveImageHeight;
        var cropW = (int)(NormalizedCropWidth * origW);
        var cropH = (int)(NormalizedCropHeight * origH);

        var pad = PaddingCalculator.Calculate(NormalizedCropX, NormalizedCropY, NormalizedCropWidth, NormalizedCropHeight, origW, origH);
        bool hasOob = PaddingCalculator.HasOutOfBounds(pad);

        var status = string.Format(Strings.StatusFormat, origW, origH, cropW, cropH, AspectRatioWidth, AspectRatioHeight);

        if (hasOob)
        {
            status += "  |  " + string.Format(Strings.StatusAIPadding, pad.Left, pad.Top, pad.Right, pad.Bottom);

            if (SelectedProvider == OutpaintProvider.StabilityAI)
            {
                var totalW = origW + pad.Left + pad.Right;
                var totalH = origH + pad.Top + pad.Bottom;
                var totalPx = (long)totalW * totalH;
                if (pad.Left > StabilityAIConfig.MaxPadding || pad.Top > StabilityAIConfig.MaxPadding || pad.Right > StabilityAIConfig.MaxPadding || pad.Bottom > StabilityAIConfig.MaxPadding)
                    status += $"  [MAX {StabilityAIConfig.MaxPadding}px!]";
                else if (totalPx > StabilityAIConfig.MaxTotalPixels)
                    status += $"  [{totalPx / 1_000_000.0:F1}MP > 9.4MP!]";
            }
        }

        StatusText = status;
    }

    [RelayCommand]
    private void SelectSourceFolder()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = Strings.DialogSelectImages,
            ShowNewFolderButton = false
        };

        if (!string.IsNullOrEmpty(SourceFolderPath))
            dialog.SelectedPath = SourceFolderPath;

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            SourceFolderPath = dialog.SelectedPath;
            SaveSettings();
            _ = LoadImagesAsync(SourceFolderPath);
        }
    }

    [RelayCommand]
    private void SelectOutputFolder()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = Strings.DialogSelectOutput,
            ShowNewFolderButton = true
        };

        if (!string.IsNullOrEmpty(OutputFolderPath))
            dialog.SelectedPath = OutputFolderPath;

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            OutputFolderPath = dialog.SelectedPath;
            SaveSettings();
        }
    }

    [RelayCommand]
    private void SetPresetRatio(string preset)
    {
        var parts = preset.Split(':');
        if (parts.Length != 2) return;
        if (!int.TryParse(parts[0], out var w) || !int.TryParse(parts[1], out var h)) return;

        UseCustomAspectRatio = true;
        AspectRatioWidth = w;
        AspectRatioHeight = h;
        ResetCropToDefault();
        UpdateStatusText();
    }

    [RelayCommand]
    private async Task Crop()
    {
        if (SelectedImage == null)
        {
            StatusText = Strings.StatusNoImage;
            return;
        }

        if (string.IsNullOrEmpty(OutputFolderPath))
        {
            SelectOutputFolder();
            if (string.IsNullOrEmpty(OutputFolderPath)) return;
        }

        // If crop extends beyond image and AI is enabled, run outpaint first
        if (HasOutOfBounds && HasAIProvider)
        {
            await OutpaintAsync();
            return;
        }

        try
        {
            var cropW = (int)(NormalizedCropWidth * EffectiveImageWidth);
            var cropH = (int)(NormalizedCropHeight * EffectiveImageHeight);
            _log.Info($"Cropping {SelectedImage.FileName} → {cropW}x{cropH}");

            var outputPath = _imageService.CropAndSave(
                SelectedImage.FilePath,
                NormalizedCropX, NormalizedCropY, NormalizedCropWidth, NormalizedCropHeight,
                OutputFolderPath!, SelectedOutputFormat, JpegQuality, _rotationDegrees);

            var fileName = Path.GetFileName(outputPath);
            SelectedImage.IsCropped = true;
            UpdateImageCounts();
            var savedMsg = string.Format(Strings.StatusSaved, fileName, cropW, cropH);
            StatusText = savedMsg;
            ShowCropSuccessToast(Strings.ToastCropSuccessTitle, savedMsg);
            _log.Success($"Saved: {outputPath}");
        }
        catch (Exception ex)
        {
            StatusText = string.Format(Strings.StatusSaveError, ex.Message);
        }
    }

    private CancellationTokenSource? _outpaintCts;

    [ObservableProperty]
    private string _outpaintTaskLabel = "";

    [ObservableProperty]
    private bool _isCropSuccessToastVisible;

    [ObservableProperty]
    private string _cropSuccessToastTitle = "";

    [ObservableProperty]
    private string _cropSuccessToastDetail = "";

    private readonly DispatcherTimer _cropSuccessToastTimer;

    [RelayCommand]
    private void CancelOutpaint()
    {
        _outpaintCts?.Cancel();
        _log.Warn("Outpainting cancelled by user.");
        StatusText = "Outpainting cancelled.";
    }

    [RelayCommand]
    private void ResetRotation() => RotationDegrees = 0;

    [RelayCommand]
    private async Task OutpaintAsync()
    {
        if (SelectedImage == null || IsOutpainting) return;

        // Capture all state before starting — user may navigate away
        var sourceFilePath = SelectedImage.FilePath;
        var sourceFileName = SelectedImage.FileName;
        var rotationDegrees = _rotationDegrees;
        var (origW, origH) = ImageService.GetRotatedDimensions(
            SelectedImage.OriginalWidth, SelectedImage.OriginalHeight, rotationDegrees);
        var provider = SelectedProvider;
        var normX = NormalizedCropX;
        var normY = NormalizedCropY;
        var normW = NormalizedCropWidth;
        var normH = NormalizedCropHeight;

        var pad = PaddingCalculator.Calculate(normX, normY, normW, normH, origW, origH);
        if (!PaddingCalculator.HasOutOfBounds(pad))
        {
            StatusText = Strings.StatusNoPadding;
            return;
        }

        if (provider == OutpaintProvider.StabilityAI && !_stabilityService.HasApiKey)
        {
            StatusText = Strings.StatusEnterApiKey;
            return;
        }
        if (provider == OutpaintProvider.OpenAI && !_openAIService.HasApiKey)
        {
            StatusText = "Enter an OpenAI API key first.";
            return;
        }

        _outpaintCts?.Cancel();
        _outpaintCts = new CancellationTokenSource();
        var ct = _outpaintCts.Token;

        IsOutpainting = true;
        OutpaintTaskLabel = $"AI: {sourceFileName}";
        _log.Info($"--- Outpainting started ({provider}) for {sourceFileName} ---");

        IProgress<string> progress = new Progress<string>(msg =>
        {
            StatusText = msg;
            OutpaintTaskLabel = $"AI: {sourceFileName} — {msg}";
            _log.Info(msg);
        });

        try
        {
            var aiOpts = new AiOutpaintOptions
            {
                UploadScalePercent = AiUploadScalePercent,
                DetailLevel = AiDetailLevel
            };

            progress.Report(Strings.StatusCropping);
            byte[] imageBytes;
            string fileName;
            if (Math.Abs(rotationDegrees) < 1e-6)
            {
                imageBytes = _imageService.LoadImageBytes(sourceFilePath);
                fileName = sourceFileName;
            }
            else
            {
                imageBytes = _imageService.GetFullImagePngBytes(sourceFilePath, rotationDegrees);
                fileName = Path.ChangeExtension(sourceFileName, ".png");
            }

            byte[] resultBytes;
            ct.ThrowIfCancellationRequested();

            if (provider == OutpaintProvider.StabilityAI)
            {
                resultBytes = await _stabilityService.OutpaintAsync(imageBytes, fileName,
                    pad.Left, pad.Top, pad.Right, pad.Bottom, UseAIPrompt, progress, aiOpts);
            }
            else if (provider == OutpaintProvider.OpenAI)
            {
                resultBytes = await _openAIService.OutpaintAsync(imageBytes, fileName,
                    pad.Left, pad.Top, pad.Right, pad.Bottom, UseAIPrompt, progress, aiOpts);
            }
            else if (provider == OutpaintProvider.HuggingFace)
            {
                resultBytes = await _huggingFaceService.OutpaintAsync(imageBytes, fileName,
                    pad.Left, pad.Top, pad.Right, pad.Bottom, UseAIPrompt, progress, aiOpts);
            }
            else
            {
                // ComfyUI: send full image — ImagePadForOutpaint handles the padding
                resultBytes = await _comfyService.OutpaintAsync(imageBytes, fileName,
                    pad.Left, pad.Top, pad.Right, pad.Bottom, UseAIPrompt, progress, aiOpts);
            }

            ct.ThrowIfCancellationRequested();

            // Save result
            progress.Report(Strings.StatusResultLoading);
            var resultImage = _imageService.LoadFromBytes(resultBytes);

            var tempDir = Path.Combine(Path.GetTempPath(), "CropCanvas");
            Directory.CreateDirectory(tempDir);
            var tempPath = Path.Combine(tempDir, $"outpaint_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            File.WriteAllBytes(tempPath, resultBytes);

            _log.Success($"Outpainting complete: {resultImage.PixelWidth}x{resultImage.PixelHeight}");
            _log.Info($"Saved to: {tempPath}");

            // Show result — load as new display image
            var newItem = new ImageItem(tempPath)
            {
                OriginalWidth = resultImage.PixelWidth,
                OriginalHeight = resultImage.PixelHeight
            };

            SelectedImage = newItem;

            var doneMsg = string.Format(Strings.StatusOutpaintDone, resultImage.PixelWidth, resultImage.PixelHeight);
            StatusText = doneMsg;
            ShowCropSuccessToast(Strings.ToastOutpaintSuccessTitle, doneMsg);
        }
        catch (OperationCanceledException)
        {
            _log.Warn("Outpainting was cancelled.");
        }
        catch (Exception ex)
        {
            StatusText = string.Format(Strings.StatusOutpaintError, ex.Message);
            _log.Error($"Outpainting failed: {ex.Message}");
        }
        finally
        {
            IsOutpainting = false;
            OutpaintTaskLabel = "";
            _log.Info("--- Outpainting ended ---");
        }
    }

    public void OnCropRegionChanged()
    {
        UpdateStatusText();
    }

    private async Task LoadImagesAsync(string folderPath)
    {
        IsLoading = true;
        Images.Clear();
        SelectedImage = null;
        DisplayImage = null;
        StatusText = Strings.StatusLoading;

        var dispatcher = Dispatcher.CurrentDispatcher;

        await Task.Run(() =>
        {
            var files = Directory.EnumerateFiles(folderPath)
                .Where(f => _imageService.IsSupportedImage(f))
                .OrderBy(f => f)
                .ToList();

            foreach (var file in files)
            {
                try
                {
                    var (w, h) = _imageService.GetImageDimensions(file);
                    var item = new ImageItem(file) { OriginalWidth = w, OriginalHeight = h };
                    var thumb = _imageService.LoadThumbnail(file);

                    dispatcher.Invoke(() =>
                    {
                        item.Thumbnail = thumb;
                        item.IsCropped = CheckIfCropped(file);
                        Images.Add(item);
                    });
                }
                catch
                {
                    // Skip unreadable files
                }
            }
        });

        IsLoading = false;
        UpdateImageCounts();
        StatusText = string.Format(Strings.StatusImagesFound, Images.Count);
    }

    private bool CheckIfCropped(string sourceFilePath)
    {
        if (string.IsNullOrEmpty(OutputFolderPath) || !Directory.Exists(OutputFolderPath))
            return false;

        var baseName = Path.GetFileNameWithoutExtension(sourceFilePath);
        return Directory.EnumerateFiles(OutputFolderPath)
            .Any(f => Path.GetFileNameWithoutExtension(f).StartsWith($"{baseName}_crop", StringComparison.OrdinalIgnoreCase));
    }

    private void UpdateImageCounts()
    {
        ImageCountText = $"{Images.Count} foto's";
        var cropped = Images.Count(i => i.IsCropped);
        CroppedCountText = cropped > 0 ? $"{cropped} klaar" : "";
    }

    private void SaveSettings()
    {
        _settings.SourceFolderPath = SourceFolderPath;
        _settings.OutputFolderPath = OutputFolderPath;
        _settings.AspectRatioWidth = AspectRatioWidth;
        _settings.AspectRatioHeight = AspectRatioHeight;
        _settings.UseCustomAspectRatio = UseCustomAspectRatio;
        _settings.OutputFormat = SelectedOutputFormat;
        _settings.JpegQuality = JpegQuality;
        _settings.OutpaintProvider = SelectedProvider;
        _settings.StabilityApiKey = StabilityApiKey;
        _settings.OpenAIApiKey = OpenAIApiKey;
        _settings.ComfyUIBaseUrl = ComfyUIBaseUrl;
        _settings.UseAIPrompt = UseAIPrompt;
        _settings.Language = SelectedLanguage;
        _settings.AiUploadScalePercent = AiUploadScalePercent;
        _settings.AiDetailLevel = AiDetailLevel;
        _settingsService.Save(_settings);
    }
}

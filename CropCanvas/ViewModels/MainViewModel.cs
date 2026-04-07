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
    private readonly ComfyUIService _comfyService = new();
    private readonly StabilityAIService _stabilityService = new();
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
    private bool _isLoading;

    [ObservableProperty]
    private bool _isOutpainting;

    [ObservableProperty]
    private bool _hasOutOfBounds;

    [ObservableProperty]
    private OutputFormat _selectedOutputFormat;

    [ObservableProperty]
    private int _jpegQuality;

    [ObservableProperty]
    private OutpaintProvider _selectedProvider;

    [ObservableProperty]
    private string? _stabilityApiKey;

    [ObservableProperty]
    private string _selectedLanguage = "nl";

    [ObservableProperty]
    private int _screenResW;

    [ObservableProperty]
    private int _screenResH;

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
        if (!string.IsNullOrEmpty(StabilityApiKey))
            _stabilityService.SetApiKey(StabilityApiKey);

        // Apply language setting
        _selectedLanguage = _settings.Language ?? "nl";
        Thread.CurrentThread.CurrentUICulture = new CultureInfo(_selectedLanguage);

        UpdateCropAspectRatio();

        // Auto-load source folder if previously set
        if (!string.IsNullOrEmpty(SourceFolderPath) && Directory.Exists(SourceFolderPath))
            _ = LoadImagesAsync(SourceFolderPath);
    }

    partial void OnAspectRatioWidthChanged(int value) => UpdateCropAspectRatio();
    partial void OnAspectRatioHeightChanged(int value) => UpdateCropAspectRatio();

    partial void OnSelectedProviderChanged(OutpaintProvider value)
    {
        OnPropertyChanged(nameof(IsStabilityProvider));
        OnPropertyChanged(nameof(HasAIProvider));
        SaveSettings();
    }

    public bool IsStabilityProvider => SelectedProvider == OutpaintProvider.StabilityAI;
    public bool HasAIProvider => SelectedProvider != OutpaintProvider.Geen;

    partial void OnStabilityApiKeyChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value))
            _stabilityService.SetApiKey(value);
        SaveSettings();
    }

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
            DisplayImage = null;
            StatusText = Strings.StatusSelectImage;
            return;
        }

        try
        {
            // DisplayImage must be set FIRST — this triggers ImageAspectRatio
            // in the overlay via code-behind, so the overlay knows the correct
            // image bounds before normalized crop coords are set.
            DisplayImage = _imageService.LoadDisplayImage(value.FilePath);
            ResetCropToDefault();
            UpdateStatusText();
        }
        catch (Exception ex)
        {
            StatusText = string.Format(Strings.StatusLoadError, ex.Message);
        }
    }

    private void ResetCropToDefault()
    {
        if (SelectedImage == null || CropAspectRatio <= 0) return;

        double imageAspect = (double)SelectedImage.OriginalWidth / SelectedImage.OriginalHeight;

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

        var origW = SelectedImage.OriginalWidth;
        var origH = SelectedImage.OriginalHeight;
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
            var outputPath = _imageService.CropAndSave(
                SelectedImage.FilePath,
                NormalizedCropX, NormalizedCropY, NormalizedCropWidth, NormalizedCropHeight,
                OutputFolderPath!, SelectedOutputFormat, JpegQuality);

            var fileName = Path.GetFileName(outputPath);
            SelectedImage.IsCropped = true;
            var cropW = (int)(NormalizedCropWidth * SelectedImage.OriginalWidth);
            var cropH = (int)(NormalizedCropHeight * SelectedImage.OriginalHeight);
            StatusText = string.Format(Strings.StatusSaved, fileName, cropW, cropH);
        }
        catch (Exception ex)
        {
            StatusText = string.Format(Strings.StatusSaveError, ex.Message);
        }
    }

    [RelayCommand]
    private async Task OutpaintAsync()
    {
        if (SelectedImage == null || IsOutpainting) return;

        try
        {
            IsOutpainting = true;
            IProgress<string> progress = new Progress<string>(msg => StatusText = msg);

            // Calculate padding in pixels
            var origW = SelectedImage.OriginalWidth;
            var origH = SelectedImage.OriginalHeight;
            var pad = PaddingCalculator.Calculate(NormalizedCropX, NormalizedCropY, NormalizedCropWidth, NormalizedCropHeight, origW, origH);
            var padLeft = pad.Left;
            var padTop = pad.Top;
            var padRight = pad.Right;
            var padBottom = pad.Bottom;

            if (!PaddingCalculator.HasOutOfBounds(pad))
            {
                StatusText = Strings.StatusNoPadding;
                return;
            }

            byte[] imageBytes;
            string fileName;
            byte[] resultBytes;

            if (SelectedProvider == OutpaintProvider.StabilityAI)
            {
                // Stability AI: send full image — the service handles scaling/aspect ratio
                if (!_stabilityService.HasApiKey)
                {
                    StatusText = Strings.StatusEnterApiKey;
                    return;
                }

                progress.Report(Strings.StatusCropping);
                imageBytes = _imageService.LoadImageBytes(SelectedImage.FilePath);
                fileName = Path.GetFileName(SelectedImage.FilePath);

                resultBytes = await _stabilityService.OutpaintAsync(imageBytes, fileName,
                    padLeft, padTop, padRight, padBottom, progress);
            }
            else
            {
                // ComfyUI: send only the visible portion (no size/ratio limits)
                var visibleX = Math.Max(0.0, NormalizedCropX);
                var visibleY = Math.Max(0.0, NormalizedCropY);
                var visibleRight = Math.Min(1.0, NormalizedCropX + NormalizedCropWidth);
                var visibleBottom = Math.Min(1.0, NormalizedCropY + NormalizedCropHeight);
                var visibleW = visibleRight - visibleX;
                var visibleH = visibleBottom - visibleY;

                progress.Report(Strings.StatusCropping);
                var filePath = SelectedImage.FilePath;
                imageBytes = await Task.Run(() => _imageService.CropToBytes(filePath,
                    visibleX, visibleY, visibleW, visibleH));
                fileName = Path.GetFileNameWithoutExtension(SelectedImage.FilePath) + "_crop.png";

                resultBytes = await _comfyService.OutpaintAsync(imageBytes, fileName,
                    padLeft, padTop, padRight, padBottom, progress);
            }

            // Load and display result
            progress.Report(Strings.StatusResultLoading);
            var resultImage = _imageService.LoadFromBytes(resultBytes);

            var tempDir = Path.Combine(Path.GetTempPath(), "CropCanvas");
            Directory.CreateDirectory(tempDir);
            var tempPath = Path.Combine(tempDir, $"outpaint_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            File.WriteAllBytes(tempPath, resultBytes);

            var newItem = new ImageItem(tempPath)
            {
                OriginalWidth = resultImage.PixelWidth,
                OriginalHeight = resultImage.PixelHeight
            };

            DisplayImage = _imageService.LoadDisplayImage(tempPath);
            _selectedImage = newItem;
            OnPropertyChanged(nameof(SelectedImage));
            ResetCropToDefault();

            StatusText = string.Format(Strings.StatusOutpaintDone, resultImage.PixelWidth, resultImage.PixelHeight);
        }
        catch (Exception ex)
        {
            StatusText = string.Format(Strings.StatusOutpaintError, ex.Message);
        }
        finally
        {
            IsOutpainting = false;
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
        _settings.Language = SelectedLanguage;
        _settingsService.Save(_settings);
    }
}

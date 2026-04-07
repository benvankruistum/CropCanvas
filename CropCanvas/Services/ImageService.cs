using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CropCanvas.Config;
using CropCanvas.Models;

namespace CropCanvas.Services;

public class ImageService
{

    public bool IsSupportedImage(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ImageConfig.SupportedExtensions.Contains(ext);
    }

    /// <summary>
    /// Gets the effective image dimensions after EXIF rotation,
    /// using BitmapImage which handles EXIF automatically.
    /// </summary>
    public (int Width, int Height) GetImageDimensions(string filePath)
    {
        // Use BitmapImage with small decode to get EXIF-corrected dimensions
        var probe = new BitmapImage();
        probe.BeginInit();
        probe.UriSource = new Uri(filePath, UriKind.Absolute);
        probe.DecodePixelWidth = 32; // Small decode just for dimensions
        probe.CacheOption = BitmapCacheOption.OnLoad;
        probe.EndInit();
        probe.Freeze();

        // Get the aspect ratio from the EXIF-corrected probe
        double aspect = (double)probe.PixelWidth / probe.PixelHeight;

        // Now get raw pixel dimensions from decoder (fast, header only)
        using var stream = File.OpenRead(filePath);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        var frame = decoder.Frames[0];
        var rawW = frame.PixelWidth;
        var rawH = frame.PixelHeight;

        // Determine if EXIF rotation swapped the dimensions
        double rawAspect = (double)rawW / rawH;
        bool isSwapped = (aspect > 1.0) != (rawAspect > 1.0);

        return isSwapped ? (rawH, rawW) : (rawW, rawH);
    }

    public BitmapSource LoadThumbnail(string filePath, int maxPixelWidth = ImageConfig.ThumbnailWidth)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
        bitmap.DecodePixelWidth = maxPixelWidth;
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    public BitmapSource LoadDisplayImage(string filePath, int maxPixelWidth = ImageConfig.DisplayWidth)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
        bitmap.DecodePixelWidth = maxPixelWidth;
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// Crops the image using normalized coordinates (0.0–1.0) and saves to output folder.
    /// The normalized coords are mapped to the actual loaded image's pixel dimensions.
    /// </summary>
    public string CropAndSave(string sourcePath, double normX, double normY, double normW, double normH,
        string outputFolder, OutputFormat format, int jpegQuality)
    {
        var source = LoadFullImage(sourcePath);
        var pw = source.PixelWidth;
        var ph = source.PixelHeight;

        // Map normalized coords to actual loaded image pixels
        var x = (int)Math.Round(normX * pw);
        var y = (int)Math.Round(normY * ph);
        var w = (int)Math.Round(normW * pw);
        var h = (int)Math.Round(normH * ph);

        // Clamp to image bounds
        x = Math.Clamp(x, 0, pw - 1);
        y = Math.Clamp(y, 0, ph - 1);
        w = Math.Clamp(w, 1, pw - x);
        h = Math.Clamp(h, 1, ph - y);

        var cropped = new CroppedBitmap(source, new Int32Rect(x, y, w, h));

        var outputExt = format == OutputFormat.Png ? ".png" : ".jpg";
        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        var outputPath = Path.Combine(outputFolder, $"{baseName}_crop{outputExt}");

        var counter = 1;
        while (File.Exists(outputPath))
        {
            outputPath = Path.Combine(outputFolder, $"{baseName}_crop_{counter}{outputExt}");
            counter++;
        }

        var quality = Math.Clamp(jpegQuality, 1, 100);
        BitmapEncoder encoder = format == OutputFormat.Png
            ? new PngBitmapEncoder()
            : new JpegBitmapEncoder { QualityLevel = quality };

        encoder.Frames.Add(BitmapFrame.Create(cropped));

        using var fileStream = File.Create(outputPath);
        encoder.Save(fileStream);

        return outputPath;
    }

    public byte[] LoadImageBytes(string filePath) => File.ReadAllBytes(filePath);

    /// <summary>
    /// Crops a region from the image and returns as PNG bytes.
    /// Uses normalized coordinates (0.0–1.0).
    /// </summary>
    public byte[] CropToBytes(string filePath, double normX, double normY, double normW, double normH)
    {
        var source = LoadFullImage(filePath);
        var pw = source.PixelWidth;
        var ph = source.PixelHeight;

        var x = Math.Clamp((int)Math.Round(normX * pw), 0, pw - 1);
        var y = Math.Clamp((int)Math.Round(normY * ph), 0, ph - 1);
        var w = Math.Clamp((int)Math.Round(normW * pw), 1, pw - x);
        var h = Math.Clamp((int)Math.Round(normH * ph), 1, ph - y);

        var cropped = new CroppedBitmap(source, new Int32Rect(x, y, w, h));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(cropped));

        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    public BitmapSource LoadFromBytes(byte[] data)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.StreamSource = new MemoryStream(data);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    public string SaveBitmapSource(BitmapSource source, string outputFolder, string baseName,
        OutputFormat format, int jpegQuality)
    {
        var outputExt = format == OutputFormat.Png ? ".png" : ".jpg";
        var outputPath = Path.Combine(outputFolder, $"{baseName}_ai{outputExt}");

        var counter = 1;
        while (File.Exists(outputPath))
        {
            outputPath = Path.Combine(outputFolder, $"{baseName}_ai_{counter}{outputExt}");
            counter++;
        }

        var quality = Math.Clamp(jpegQuality, 1, 100);
        BitmapEncoder encoder = format == OutputFormat.Png
            ? new PngBitmapEncoder()
            : new JpegBitmapEncoder { QualityLevel = quality };

        encoder.Frames.Add(BitmapFrame.Create(source));
        using var fileStream = File.Create(outputPath);
        encoder.Save(fileStream);
        return outputPath;
    }

    private BitmapSource LoadFullImage(string filePath)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}

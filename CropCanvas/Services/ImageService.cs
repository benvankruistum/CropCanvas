using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CropCanvas.Config;
using CropCanvas.Models;
using ImageMagick;

namespace CropCanvas.Services;

public class ImageService
{
    /// <summary>Pixel size after rotating a <paramref name="width"/>×<paramref name="height"/> bitmap by <paramref name="degrees"/>.</summary>
    public static (int Width, int Height) GetRotatedDimensions(int width, int height, double degrees)
    {
        if (width <= 0 || height <= 0) return (0, 0);
        var rad = degrees * (Math.PI / 180.0);
        var cos = Math.Abs(Math.Cos(rad));
        var sin = Math.Abs(Math.Sin(rad));
        var rw = (int)Math.Ceiling(width * cos + height * sin);
        var rh = (int)Math.Ceiling(width * sin + height * cos);
        return (Math.Max(1, rw), Math.Max(1, rh));
    }

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
        if (IsHeifPath(filePath))
            return GetHeifDimensions(filePath);

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
        if (IsHeifPath(filePath))
            return LoadHeifScaled(filePath, maxPixelWidth);
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
        => LoadDisplayImage(filePath, maxPixelWidth, 0);

    /// <param name="rotationDegrees">Straightening (±<see cref="ImageConfig.RotationMaxDegrees"/>°), baked into the bitmap.</param>
    public BitmapSource LoadDisplayImage(string filePath, int maxPixelWidth, double rotationDegrees)
    {
        BitmapSource loaded;
        if (IsHeifPath(filePath))
            loaded = LoadHeifScaled(filePath, maxPixelWidth);
        else
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
            bitmap.DecodePixelWidth = maxPixelWidth;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.EndInit();
            bitmap.Freeze();
            loaded = bitmap;
        }

        rotationDegrees = Math.Clamp(rotationDegrees, -ImageConfig.RotationMaxDegrees, ImageConfig.RotationMaxDegrees);
        if (Math.Abs(rotationDegrees) < 1e-6)
            return loaded;

        // Rotation expands the bounding box; without scaling, the preview bitmap is larger in pixels than the
        // unrotated decode → Uniform stretch lays out a smaller on-screen image (feels like zooming out when reset).
        var maxLongest = Math.Max(loaded.PixelWidth, loaded.PixelHeight);
        var rotated = RotateBitmap(loaded, rotationDegrees);
        return ScaleUniformToMaxLongestEdge(rotated, maxLongest);
    }

    /// <summary>Uniform scale so max(width,height) ≤ <paramref name="maxLongestEdge"/> (keeps preview size stable when rotating).</summary>
    private static BitmapSource ScaleUniformToMaxLongestEdge(BitmapSource source, int maxLongestEdge)
    {
        if (maxLongestEdge <= 0) return source;
        var w = source.PixelWidth;
        var h = source.PixelHeight;
        var longest = Math.Max(w, h);
        if (longest <= maxLongestEdge)
            return source;

        var scale = maxLongestEdge / (double)longest;
        var scaled = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        scaled.Freeze();
        return scaled;
    }

    /// <summary>PNG bytes of the full-resolution image, optionally rotated (for AI pipelines that must match crop space).</summary>
    public byte[] GetFullImagePngBytes(string filePath, double rotationDegrees)
    {
        var source = LoadFullImage(filePath);
        rotationDegrees = Math.Clamp(rotationDegrees, -ImageConfig.RotationMaxDegrees, ImageConfig.RotationMaxDegrees);
        if (Math.Abs(rotationDegrees) >= 1e-6)
            source = RotateBitmap(source, rotationDegrees);
        return EncodePngBytes(source);
    }

    public static BitmapSource RotateBitmap(BitmapSource source, double degrees)
    {
        if (Math.Abs(degrees) < 1e-9)
            return source;

        var w = source.PixelWidth;
        var h = source.PixelHeight;
        var (rw, rh) = GetRotatedDimensions(w, h, degrees);
        var pbgra = EnsurePbgra32(source);

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.PushTransform(new TranslateTransform(rw / 2.0, rh / 2.0));
            dc.PushTransform(new RotateTransform(degrees));
            dc.PushTransform(new TranslateTransform(-w / 2.0, -h / 2.0));
            dc.DrawImage(pbgra, new Rect(0, 0, w, h));
            dc.Pop();
            dc.Pop();
            dc.Pop();
        }

        var rtb = new RenderTargetBitmap(rw, rh, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    private static BitmapSource EnsurePbgra32(BitmapSource source)
    {
        if (source.Format == PixelFormats.Pbgra32)
            return source;
        var conv = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        conv.Freeze();
        return conv;
    }

    private static byte[] EncodePngBytes(BitmapSource source)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Crops the image using normalized coordinates (0.0–1.0) and saves to output folder.
    /// The normalized coords are mapped to the actual loaded image's pixel dimensions.
    /// </summary>
    public string CropAndSave(string sourcePath, double normX, double normY, double normW, double normH,
        string outputFolder, OutputFormat format, int jpegQuality, double rotationDegrees = 0)
    {
        var source = LoadFullImage(sourcePath);
        rotationDegrees = Math.Clamp(rotationDegrees, -ImageConfig.RotationMaxDegrees, ImageConfig.RotationMaxDegrees);
        if (Math.Abs(rotationDegrees) >= 1e-6)
            source = RotateBitmap(source, rotationDegrees);

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
    public byte[] CropToBytes(string filePath, double normX, double normY, double normW, double normH,
        double rotationDegrees = 0)
    {
        var source = LoadFullImage(filePath);
        rotationDegrees = Math.Clamp(rotationDegrees, -ImageConfig.RotationMaxDegrees, ImageConfig.RotationMaxDegrees);
        if (Math.Abs(rotationDegrees) >= 1e-6)
            source = RotateBitmap(source, rotationDegrees);

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
        if (LooksLikeHeifBytes(data))
            return MagickBytesToBitmapSource(data);

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
        if (IsHeifPath(filePath))
            return LoadHeifFull(filePath);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static bool IsHeifPath(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext is ".heic" or ".heif";
    }

    private static (int Width, int Height) GetHeifDimensions(string filePath)
    {
        using var image = new MagickImage();
        image.Ping(filePath);
        return ((int)image.Width, (int)image.Height);
    }

    private static BitmapSource LoadHeifScaled(string filePath, int maxPixelWidth)
    {
        using var image = new MagickImage(filePath);
        image.AutoOrient();
        image.ColorSpace = ColorSpace.sRGB;
        if (image.Width > (uint)maxPixelWidth)
            image.Resize(new MagickGeometry((uint)maxPixelWidth, 0) { IgnoreAspectRatio = false });
        return MagickToBitmapSource(image);
    }

    private static BitmapSource LoadHeifFull(string filePath)
    {
        using var image = new MagickImage(filePath);
        image.AutoOrient();
        image.ColorSpace = ColorSpace.sRGB;
        return MagickToBitmapSource(image);
    }

    /// <summary>
    /// Decodes HEIF/HEIC via ImageMagick to a frozen <see cref="BitmapSource"/> (PNG bridge).
    /// </summary>
    private static BitmapSource MagickToBitmapSource(MagickImage image)
    {
        using var ms = new MemoryStream();
        image.Write(ms, MagickFormat.Png);
        ms.Position = 0;
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.StreamSource = ms;
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource MagickBytesToBitmapSource(byte[] data)
    {
        using var image = new MagickImage(data);
        image.AutoOrient();
        image.ColorSpace = ColorSpace.sRGB;
        return MagickToBitmapSource(image);
    }

    /// <summary>ISO BMFF <c>ftyp</c> brands used by HEIF/HEIC.</summary>
    private static bool LooksLikeHeifBytes(byte[] data)
    {
        if (data.Length < 12) return false;
        if (data[4] != (byte)'f' || data[5] != (byte)'t' || data[6] != (byte)'y' || data[7] != (byte)'p')
            return false;
        var brand = Encoding.ASCII.GetString(data, 8, 4);
        return brand is "heic" or "heix" or "hevc" or "hevx" or "mif1" or "msf1" or "avif";
    }
}

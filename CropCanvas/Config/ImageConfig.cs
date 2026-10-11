namespace CropCanvas.Config;

public static class ImageConfig
{
    public static readonly string[] SupportedExtensions =
    [
        ".jpg", ".jpeg", ".png", ".bmp", ".webp",
        ".heic", ".heif",
    ];
    public const int ThumbnailWidth = 150;
    public const int DisplayWidth = 1400;
    public const int DefaultJpegQuality = 95;

    /// <summary>Max absolute rotation (degrees) for straightening — finishing touches only.</summary>
    public const double RotationMaxDegrees = 3.0;
}

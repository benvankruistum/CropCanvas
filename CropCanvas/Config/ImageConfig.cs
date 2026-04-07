namespace CropCanvas.Config;

public static class ImageConfig
{
    public static readonly string[] SupportedExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".webp"];
    public const int ThumbnailWidth = 150;
    public const int DisplayWidth = 1400;
    public const int DefaultJpegQuality = 95;
}

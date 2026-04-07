namespace CropCanvas.Models;

public enum OutputFormat
{
    Jpeg,
    Png
}

public enum OutpaintProvider
{
    Geen,
    ComfyUI,
    StabilityAI
}

public class AppSettings
{
    public string? SourceFolderPath { get; set; }
    public string? OutputFolderPath { get; set; }
    public int AspectRatioWidth { get; set; } = 16;
    public int AspectRatioHeight { get; set; } = 9;
    public bool UseCustomAspectRatio { get; set; }
    public OutputFormat OutputFormat { get; set; } = OutputFormat.Jpeg;
    public int JpegQuality { get; set; } = 95;
    public OutpaintProvider OutpaintProvider { get; set; } = OutpaintProvider.Geen;
    public string? StabilityApiKey { get; set; }
    public string Language { get; set; } = "nl"; // Default Dutch
}

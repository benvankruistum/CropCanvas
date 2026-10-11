using CropCanvas.Config;

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
    StabilityAI,
    OpenAI,
    HuggingFace
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
    public string? OpenAIApiKey { get; set; }
    /// <summary>HTTP or HTTPS base URL for ComfyUI (e.g. http://127.0.0.1:8188).</summary>
    public string ComfyUIBaseUrl { get; set; } = ComfyUIConfig.DefaultBaseUrl;
    public bool UseAIPrompt { get; set; } = true;
    public string Language { get; set; } = "nl"; // Default Dutch

    /// <summary>50–100: AI upload resolution as % of full-size image/canvas (100 = best quality, no extra downscale).</summary>
    public int AiUploadScalePercent { get; set; } = 100;

    /// <summary>1–100: sampling/detail — interpreted per provider (Comfy steps, HF steps, Stability creativity).</summary>
    public int AiDetailLevel { get; set; } = 50;
}

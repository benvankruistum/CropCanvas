namespace CropCanvas.Config;

public static class ComfyUIConfig
{
    public const string BaseUrl = "http://127.0.0.1:8188";
    public const int WebSocketBufferSize = 65536;
    public const int TimeoutMinutes = 5;
    public const int PollIntervalMs = 1000;
    public const int MaxPollAttempts = 300;

    // Sampling parameters optimized for SDXL inpainting
    public const int DefaultSteps = 30;
    public const double DefaultCfg = 7.0;
    public const double DefaultDenoise = 1.0;
    public const int DefaultFeathering = 40;
    public const int GrowMaskBy = 16;
    public const string DefaultSampler = "euler_ancestral";
    public const string DefaultScheduler = "karras";

    // Prompts
    public const string PositivePrompt =
        "high quality, highly detailed, photorealistic, natural continuation of the scene, " +
        "seamless extension, consistent lighting, consistent color palette, 8k, sharp focus";
    public const string NegativePrompt =
        "blurry, artifacts, visible seam, border, low quality, watermark, text, logo, " +
        "distorted, deformed, disfigured, bad anatomy, duplicate, out of focus";
}

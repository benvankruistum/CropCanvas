namespace CropCanvas.Config;

public static class ComfyUIConfig
{
    /// <summary>Default HTTP(S) base URL for the ComfyUI server (no trailing slash).</summary>
    public const string DefaultBaseUrl = "http://127.0.0.1:8188";
    public const int WebSocketBufferSize = 65536;
    public const int TimeoutMinutes = 10;
    public const int PollIntervalMs = 1000;
    public const int MaxPollAttempts = 300;

    // ControlNet Union workflow (best quality)
    public const string ControlNetModel = "xinsir_union_promax.safetensors";
    public const string PreferredCheckpoint = "RealVisXL_V5.0_Lightning";
    public const int DefaultSteps = 26;
    public const double DefaultCfg = 4.0;
    public const double DefaultDenoise = 1.0;
    public const int GrowMaskBy = 30;
    public const string DefaultSampler = "dpmpp_2m";
    public const string DefaultScheduler = "karras";

    // Prompts
    public const string PositivePrompt =
        "seamless natural extension, empty background continuation, " +
        "matching texture and color, subtle, unnoticeable, plain, " +
        "consistent lighting, same atmosphere, photorealistic";
    public const string NegativePrompt =
        "new objects, people, animals, faces, buildings, signs, text, logo, watermark, " +
        "interesting details, focal point, center of attention, " +
        "different style, different lighting, different season, color mismatch, " +
        "visible seam, border, artifacts, blurry, distorted, deformed";
}

namespace CropCanvas.Models;

/// <summary>
/// User-tunable options for outpainting. Providers interpret <see cref="DetailLevel"/> differently
/// (ComfyUI: sampler steps, HuggingFace: diffusion steps, Stability AI: creativity).
/// </summary>
public sealed class AiOutpaintOptions
{
    /// <summary>50–100: longest edge of the upload relative to full resolution (100 = no downscale from this setting).</summary>
    public int UploadScalePercent { get; init; } = 100;

    public static int ClampUploadScalePercent(int value) => Math.Clamp(value, 50, 100);

    /// <summary>JPEG quality 1–100 when re-encoding scaled uploads (e.g. ComfyUI).</summary>
    public int UploadJpegQuality { get; init; } = 95;

    /// <summary>1–100: render detail — meaning depends on <see cref="OutpaintProvider"/>.</summary>
    public int DetailLevel { get; init; } = 50;

    public static int ClampDetailLevel(int value) => Math.Clamp(value, 1, 100);

    /// <summary>ComfyUI KSampler steps (approx. 12–40).</summary>
    public int ComfySamplerSteps =>
        12 + (int)Math.Round((ClampDetailLevel(DetailLevel) - 1) / 99.0 * 28);

    /// <summary>HuggingFace Gradio <c>num_inference_steps</c> (approx. 4–24).</summary>
    public int HuggingFaceInferenceSteps =>
        4 + (int)Math.Round((ClampDetailLevel(DetailLevel) - 1) / 99.0 * 20);

    /// <summary>Stability outpaint creativity (conservative range).</summary>
    public double StabilityCreativity =>
        Math.Clamp(0.08 + ClampDetailLevel(DetailLevel) / 100.0 * 0.38, 0.05, 0.5);
}

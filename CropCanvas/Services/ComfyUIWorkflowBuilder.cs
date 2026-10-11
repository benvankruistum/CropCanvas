using CropCanvas.Config;

namespace CropCanvas.Services;

/// <summary>
/// Builds ComfyUI workflow JSON for outpainting.
///
/// ControlNet Union workflow:
/// ImagePadForOutpaint (feathering=0) produces black-padded image + mask.
/// The padded image (with black fill areas) goes directly to ControlNet Union "repaint".
/// After generation, the original is composited back to preserve untouched areas.
/// </summary>
public static class ComfyUIWorkflowBuilder
{
    public static Dictionary<string, object> Build(string imageName,
        int padLeft, int padTop, int padRight, int padBottom, string checkpoint,
        string? positivePrompt = null, string? negativePrompt = null)
    {
        var pos = positivePrompt ?? ComfyUIConfig.PositivePrompt;
        var neg = negativePrompt ?? ComfyUIConfig.NegativePrompt;

        return new Dictionary<string, object>
        {
            // Checkpoint
            ["1"] = new
            {
                class_type = "CheckpointLoaderSimple",
                inputs = new { ckpt_name = checkpoint }
            },

            // Source image
            ["2"] = new
            {
                class_type = "LoadImage",
                inputs = new { image = imageName }
            },

            // Pad with feathering=0 → black fill areas + mask
            ["3"] = new
            {
                class_type = "ImagePadForOutpaint",
                inputs = new
                {
                    image = new object[] { "2", 0 },
                    left = padLeft,
                    top = padTop,
                    right = padRight,
                    bottom = padBottom,
                    feathering = 0
                }
            },

            // Prompts
            ["4"] = new
            {
                class_type = "CLIPTextEncode",
                inputs = new { text = pos, clip = new object[] { "1", 1 } }
            },
            ["5"] = new
            {
                class_type = "CLIPTextEncode",
                inputs = new { text = neg, clip = new object[] { "1", 1 } }
            },

            // ControlNet Union ProMax in "repaint" mode
            ["10"] = new
            {
                class_type = "ControlNetLoader",
                inputs = new { control_net_name = ComfyUIConfig.ControlNetModel }
            },
            ["11"] = new
            {
                class_type = "SetUnionControlNetType",
                inputs = new
                {
                    control_net = new object[] { "10", 0 },
                    type = "repaint"
                }
            },

            // Apply ControlNet — padded image already has black fill areas (RGB 0,0,0)
            ["12"] = new
            {
                class_type = "ControlNetApplyAdvanced",
                inputs = new
                {
                    positive = new object[] { "4", 0 },
                    negative = new object[] { "5", 0 },
                    control_net = new object[] { "11", 0 },
                    image = new object[] { "3", 0 },   // padded image with black areas
                    strength = 1.0,
                    start_percent = 0.0,
                    end_percent = 1.0
                }
            },

            // VAE encode for inpainting with the outpaint mask
            ["6"] = new
            {
                class_type = "VAEEncodeForInpaint",
                inputs = new
                {
                    pixels = new object[] { "3", 0 },   // padded image
                    vae = new object[] { "1", 2 },
                    mask = new object[] { "3", 1 },      // outpaint mask from pad node
                    grow_mask_by = ComfyUIConfig.GrowMaskBy
                }
            },

            // Sample
            ["7"] = new
            {
                class_type = "KSampler",
                inputs = new
                {
                    model = new object[] { "1", 0 },
                    positive = new object[] { "12", 0 },
                    negative = new object[] { "12", 1 },
                    latent_image = new object[] { "6", 0 },
                    seed = Random.Shared.Next(),
                    steps = ComfyUIConfig.DefaultSteps,
                    cfg = ComfyUIConfig.DefaultCfg,
                    sampler_name = ComfyUIConfig.DefaultSampler,
                    scheduler = ComfyUIConfig.DefaultScheduler,
                    denoise = ComfyUIConfig.DefaultDenoise
                }
            },

            // Decode
            ["8"] = new
            {
                class_type = "VAEDecode",
                inputs = new
                {
                    samples = new object[] { "7", 0 },
                    vae = new object[] { "1", 2 }
                }
            },

            // Composite original back onto result to preserve untouched areas
            // InvertMask gives us the original image area
            ["20"] = new
            {
                class_type = "InvertMask",
                inputs = new { mask = new object[] { "3", 1 } }
            },
            ["30"] = new
            {
                class_type = "ImageCompositeMasked",
                inputs = new
                {
                    destination = new object[] { "8", 0 },
                    source = new object[] { "3", 0 },
                    mask = new object[] { "20", 0 },
                    x = 0,
                    y = 0,
                    resize_source = false
                }
            },

            // Save
            ["9"] = new
            {
                class_type = "SaveImage",
                inputs = new
                {
                    images = new object[] { "30", 0 },
                    filename_prefix = "outpaint_result"
                }
            }
        };
    }

    /// <summary>
    /// Simpler fallback workflow without ControlNet.
    /// </summary>
    public static Dictionary<string, object> BuildSimple(string imageName,
        int padLeft, int padTop, int padRight, int padBottom, string checkpoint,
        string? positivePrompt = null, string? negativePrompt = null, int samplerSteps = 30)
    {
        var pos = positivePrompt ?? ComfyUIConfig.PositivePrompt;
        var neg = negativePrompt ?? ComfyUIConfig.NegativePrompt;

        return new Dictionary<string, object>
        {
            ["1"] = new { class_type = "CheckpointLoaderSimple", inputs = new { ckpt_name = checkpoint } },
            ["2"] = new { class_type = "LoadImage", inputs = new { image = imageName } },
            ["3"] = new
            {
                class_type = "ImagePadForOutpaint",
                inputs = new { image = new object[] { "2", 0 }, left = padLeft, top = padTop, right = padRight, bottom = padBottom, feathering = 0 }
            },
            ["4"] = new { class_type = "CLIPTextEncode", inputs = new { text = pos, clip = new object[] { "1", 1 } } },
            ["5"] = new { class_type = "CLIPTextEncode", inputs = new { text = neg, clip = new object[] { "1", 1 } } },
            ["6"] = new
            {
                class_type = "VAEEncodeForInpaint",
                inputs = new { pixels = new object[] { "3", 0 }, vae = new object[] { "1", 2 }, mask = new object[] { "3", 1 }, grow_mask_by = 48 }
            },
            ["7"] = new
            {
                class_type = "KSampler",
                inputs = new
                {
                    model = new object[] { "1", 0 }, positive = new object[] { "4", 0 }, negative = new object[] { "5", 0 },
                    latent_image = new object[] { "6", 0 }, seed = Random.Shared.Next(),
                    steps = Math.Clamp(samplerSteps, 8, 50), cfg = 4.5, sampler_name = "euler_ancestral", scheduler = "karras", denoise = 1.0
                }
            },
            ["8"] = new { class_type = "VAEDecode", inputs = new { samples = new object[] { "7", 0 }, vae = new object[] { "1", 2 } } },
            ["9"] = new { class_type = "SaveImage", inputs = new { images = new object[] { "8", 0 }, filename_prefix = "outpaint_result" } }
        };
    }
}

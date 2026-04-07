using CropCanvas.Config;

namespace CropCanvas.Services;

public static class ComfyUIWorkflowBuilder
{
    public static Dictionary<string, object> Build(string imageName,
        int padLeft, int padTop, int padRight, int padBottom, string checkpoint,
        string? positivePrompt = null, string? negativePrompt = null)
    {
        return new Dictionary<string, object>
        {
            ["1"] = new
            {
                class_type = "CheckpointLoaderSimple",
                inputs = new { ckpt_name = checkpoint }
            },
            ["2"] = new
            {
                class_type = "LoadImage",
                inputs = new { image = imageName }
            },
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
                    feathering = ComfyUIConfig.DefaultFeathering
                }
            },
            ["4"] = new
            {
                class_type = "CLIPTextEncode",
                inputs = new
                {
                    text = positivePrompt ?? ComfyUIConfig.PositivePrompt,
                    clip = new object[] { "1", 1 }
                }
            },
            ["5"] = new
            {
                class_type = "CLIPTextEncode",
                inputs = new
                {
                    text = negativePrompt ?? ComfyUIConfig.NegativePrompt,
                    clip = new object[] { "1", 1 }
                }
            },
            ["6"] = new
            {
                class_type = "VAEEncodeForInpaint",
                inputs = new
                {
                    pixels = new object[] { "3", 0 },
                    vae = new object[] { "1", 2 },
                    mask = new object[] { "3", 1 },
                    grow_mask_by = ComfyUIConfig.GrowMaskBy
                }
            },
            ["7"] = new
            {
                class_type = "KSampler",
                inputs = new
                {
                    model = new object[] { "1", 0 },
                    positive = new object[] { "4", 0 },
                    negative = new object[] { "5", 0 },
                    latent_image = new object[] { "6", 0 },
                    seed = Random.Shared.Next(),
                    steps = ComfyUIConfig.DefaultSteps,
                    cfg = ComfyUIConfig.DefaultCfg,
                    sampler_name = ComfyUIConfig.DefaultSampler,
                    scheduler = ComfyUIConfig.DefaultScheduler,
                    denoise = ComfyUIConfig.DefaultDenoise
                }
            },
            ["8"] = new
            {
                class_type = "VAEDecode",
                inputs = new
                {
                    samples = new object[] { "7", 0 },
                    vae = new object[] { "1", 2 }
                }
            },
            ["9"] = new
            {
                class_type = "SaveImage",
                inputs = new
                {
                    images = new object[] { "8", 0 },
                    filename_prefix = "outpaint_result"
                }
            }
        };
    }
}

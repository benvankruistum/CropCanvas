namespace CropCanvas.Config;

public static class StabilityAIConfig
{
    public const string BaseUrl = "https://api.stability.ai";
    public const string OutpaintEndpoint = "/v2beta/stable-image/edit/outpaint";
    public const int MaxPadding = 2000;
    public const long MaxTotalPixels = 9_437_184;
    public const double MinAspect = 1.0 / 2.5;
    public const double MaxAspect = 2.5;
    public const double DefaultCreativity = 0.5;
}

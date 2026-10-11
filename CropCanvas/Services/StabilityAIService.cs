using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CropCanvas.Config;
using CropCanvas.Models;
using CropCanvas.Resources;
using CropCanvas.Services.Interfaces;

namespace CropCanvas.Services;

public class StabilityAIService : IOutpaintProvider
{
    private readonly HttpClient _http = new()
    {
        BaseAddress = new Uri(StabilityAIConfig.BaseUrl),
        Timeout = TimeSpan.FromMinutes(5)
    };

    public void SetApiKey(string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            _http.DefaultRequestHeaders.Authorization = null;
        else
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
    }

    public bool HasApiKey => _http.DefaultRequestHeaders.Authorization?.Parameter is { Length: > 0 };

    public async Task<byte[]> OutpaintAsync(byte[] imageData, string filename,
        int padLeft, int padTop, int padRight, int padBottom,
        bool usePrompt = true, IProgress<string>? progress = null, AiOutpaintOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (!HasApiKey)
            throw new InvalidOperationException(Strings.StabilityNoKey);

        if (padLeft == 0 && padTop == 0 && padRight == 0 && padBottom == 0)
            throw new InvalidOperationException(Strings.StabilityNoPadding);

        var opt = options ?? new AiOutpaintOptions();
        var pct = AiOutpaintOptions.ClampUploadScalePercent(opt.UploadScalePercent);
        var jpegQPreference = Math.Clamp(opt.UploadJpegQuality, 70, 100);
        var creativity = opt.StabilityCreativity;

        // Load image to get dimensions
        var bmp = LoadBitmap(imageData);
        var origW = bmp.PixelWidth;
        var origH = bmp.PixelHeight;

        // Optional user downscale: longest canvas edge × (upload % / 100). At 100% no user downscale.
        var canvasW0 = origW + padLeft + padRight;
        var canvasH0 = origH + padTop + padBottom;
        var maxCanvasEdge = Math.Max(canvasW0, canvasH0);
        var allowedEdge = (int)Math.Round(maxCanvasEdge * (pct / 100.0));
        if (maxCanvasEdge > allowedEdge && pct < 100)
        {
            var pre = (double)allowedEdge / maxCanvasEdge;
            progress?.Report($"Scaling to {pct}% of original ({allowedEdge}px longest edge) for upload…");
            origW = Math.Max(1, (int)(origW * pre));
            origH = Math.Max(1, (int)(origH * pre));
            padLeft = (int)Math.Round(padLeft * pre);
            padTop = (int)Math.Round(padTop * pre);
            padRight = (int)Math.Round(padRight * pre);
            padBottom = (int)Math.Round(padBottom * pre);
            imageData = ScaleImage(bmp, origW, origH);
            bmp = LoadBitmap(imageData);
        }

        // Fix aspect ratio if outside 1:2.5 to 2.5:1 limits
        var totalW = origW + padLeft + padRight;
        var totalH = origH + padTop + padBottom;
        var aspect = (double)totalW / totalH;

        if (aspect > StabilityAIConfig.MaxAspect)
        {
            // Too wide — add vertical padding to bring ratio within limits
            var neededH = (int)Math.Ceiling(totalW / StabilityAIConfig.MaxAspect);
            var extraH = neededH - totalH;
            padTop += extraH / 2;
            padBottom += (extraH + 1) / 2;
            progress?.Report(string.Format(Strings.StabilityRatioWide, aspect, extraH));
        }
        else if (aspect < StabilityAIConfig.MinAspect)
        {
            // Too tall — add horizontal padding
            var neededW = (int)Math.Ceiling(totalH * StabilityAIConfig.MinAspect);
            var extraW = neededW - totalW;
            padLeft += extraW / 2;
            padRight += (extraW + 1) / 2;
            progress?.Report(string.Format(Strings.StabilityRatioTall, extraW));
        }

        // Recalculate after aspect fix
        totalW = origW + padLeft + padRight;
        totalH = origH + padTop + padBottom;

        // Calculate scale factor if needed to fit within API limits
        var scale = CalculateScaleFactor(origW, origH, padLeft, padTop, padRight, padBottom);

        if (scale < 1.0)
        {
            progress?.Report(string.Format(Strings.StabilityScaling, scale));

            var scaledW = (int)(origW * scale);
            var scaledH = (int)(origH * scale);
            imageData = ScaleImage(bmp, scaledW, scaledH);

            padLeft = (int)Math.Round(padLeft * scale);
            padTop = (int)Math.Round(padTop * scale);
            padRight = (int)Math.Round(padRight * scale);
            padBottom = (int)Math.Round(padBottom * scale);

            if (padLeft == 0 && padTop == 0 && padRight == 0 && padBottom == 0)
                padLeft = 1;

            progress?.Report(string.Format(Strings.StabilityScaled, scaledW, scaledH, padLeft, padTop, padRight, padBottom));
        }

        // Ensure image is under 10MB API limit — re-encode as JPEG if needed
        const int maxBytes = 10 * 1024 * 1024;
        if (imageData.Length > maxBytes)
        {
            progress?.Report($"Image {imageData.Length / 1_048_576.0:F1}MB > 10MB limit — compressing...");
            var currentBmp = scale < 1.0 ? LoadBitmap(imageData) : bmp;

            // Try JPEG quality levels until under 10MB (start near user preference)
            var startQ = Math.Clamp(jpegQPreference, 30, 95);
            for (int quality = startQ; quality >= 30; quality -= 10)
            {
                imageData = CompressAsJpeg(currentBmp, quality);
                if (imageData.Length <= maxBytes)
                {
                    progress?.Report($"Compressed to {imageData.Length / 1_048_576.0:F1}MB (JPEG quality {quality})");
                    break;
                }
            }

            // If still too large, scale down further
            if (imageData.Length > maxBytes)
            {
                var extraScale = Math.Sqrt((double)maxBytes / imageData.Length) * 0.9;
                var currentW = scale < 1.0 ? (int)(origW * scale) : origW;
                var currentH = scale < 1.0 ? (int)(origH * scale) : origH;
                var newW = (int)(currentW * extraScale);
                var newH = (int)(currentH * extraScale);
                imageData = ScaleImage(currentBmp, newW, newH);
                imageData = CompressAsJpeg(LoadBitmap(imageData), 85);
                progress?.Report($"Scaled + compressed to {imageData.Length / 1_048_576.0:F1}MB ({newW}x{newH})");
            }
        }

        // Clamp padding to API max
        padLeft = Math.Clamp(padLeft, 0, StabilityAIConfig.MaxPadding);
        padTop = Math.Clamp(padTop, 0, StabilityAIConfig.MaxPadding);
        padRight = Math.Clamp(padRight, 0, StabilityAIConfig.MaxPadding);
        padBottom = Math.Clamp(padBottom, 0, StabilityAIConfig.MaxPadding);

        // Analyze image for context-aware prompt (optional)
        string? prompt = null;
        if (usePrompt)
        {
            progress?.Report("Analyzing image...");
            var analysis = ImageAnalyzer.Analyze(imageData);
            prompt = ImageAnalyzer.GeneratePrompt(analysis);
            progress?.Report($"Detected: {analysis.TimeOfDay}, {analysis.Season}, {analysis.Mood}");
        }
        else
        {
            progress?.Report("Skipping prompt — AI will use visual context only.");
        }

        progress?.Report(string.Format(Strings.StabilitySending, padLeft, padTop, padRight, padBottom));

        var boundary = $"----StabilityBoundary{Guid.NewGuid():N}";
        using var content = new MultipartFormDataContent(boundary);

        // Detect content type based on image data header
        var isJpeg = imageData.Length > 2 && imageData[0] == 0xFF && imageData[1] == 0xD8;
        var contentType = isJpeg ? "image/jpeg" : "image/png";
        var fileExt = isJpeg ? "jpg" : "png";

        var imageContent = new ByteArrayContent(imageData);
        imageContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        imageContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "\"image\"",
            FileName = $"\"outpaint_input.{fileExt}\""
        };
        content.Add(imageContent);

        if (padLeft > 0) AddFormField(content, "left", padLeft.ToString());
        if (padTop > 0) AddFormField(content, "up", padTop.ToString());
        if (padRight > 0) AddFormField(content, "right", padRight.ToString());
        if (padBottom > 0) AddFormField(content, "down", padBottom.ToString());

        if (!string.IsNullOrEmpty(prompt))
            AddFormField(content, "prompt", prompt);
        AddFormField(content, "creativity", creativity.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddFormField(content, "output_format", "png");

        progress?.Report(Strings.StabilityProcessing);

        using var request = new HttpRequestMessage(HttpMethod.Post, StabilityAIConfig.OutpaintEndpoint);
        request.Content = content;
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));

        var resp = await _http.SendAsync(request, cancellationToken);

        if (!resp.IsSuccessStatusCode)
        {
            var error = await resp.Content.ReadAsStringAsync();
            throw new Exception(string.Format(Strings.StabilityError, resp.StatusCode, error));
        }

        progress?.Report(Strings.StabilityDownloading);
        var resultBytes = await resp.Content.ReadAsByteArrayAsync();

        progress?.Report(Strings.StabilityDone);
        return resultBytes;
    }

    private static double CalculateScaleFactor(int imgW, int imgH, int pL, int pT, int pR, int pB)
    {
        var totalW = imgW + pL + pR;
        var totalH = imgH + pT + pB;
        var totalPixels = (long)totalW * totalH;

        var scale = 1.0;

        // Scale down if total pixels exceed limit
        if (totalPixels > StabilityAIConfig.MaxTotalPixels)
            scale = Math.Sqrt((double)StabilityAIConfig.MaxTotalPixels / totalPixels);

        // Scale down if any padding exceeds max
        var maxPad = Math.Max(Math.Max(pL, pR), Math.Max(pT, pB));
        if (maxPad > StabilityAIConfig.MaxPadding)
            scale = Math.Min(scale, (double)StabilityAIConfig.MaxPadding / maxPad);

        // Check aspect ratio after scaling (aspect doesn't change with uniform scaling)
        var aspect = (double)totalW / totalH;
        if (aspect < StabilityAIConfig.MinAspect || aspect > StabilityAIConfig.MaxAspect)
        {
            // Can't fix aspect ratio by scaling — return current scale
            // The API will reject, but we show a clear error
        }

        return scale;
    }

    private static BitmapSource LoadBitmap(byte[] data)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.StreamSource = new MemoryStream(data);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private static byte[] ScaleImage(BitmapSource source, int targetWidth, int targetHeight)
    {
        var scaleX = (double)targetWidth / source.PixelWidth;
        var scaleY = (double)targetHeight / source.PixelHeight;
        var scaled = new TransformedBitmap(source, new ScaleTransform(scaleX, scaleY));

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(scaled));

        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static byte[] CompressAsJpeg(BitmapSource source, int quality)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static void AddFormField(MultipartFormDataContent content, string name, string value)
    {
        var field = new StringContent(value, Encoding.UTF8);
        field.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = $"\"{name}\""
        };
        field.Headers.ContentType = null;
        content.Add(field);
    }
}

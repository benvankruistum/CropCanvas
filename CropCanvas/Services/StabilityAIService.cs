using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CropCanvas.Config;
using CropCanvas.Resources;
using CropCanvas.Services.Interfaces;

namespace CropCanvas.Services;

public class StabilityAIService : IOutpaintProvider
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri(StabilityAIConfig.BaseUrl) };

    public void SetApiKey(string apiKey)
    {
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public bool HasApiKey => _http.DefaultRequestHeaders.Authorization?.Parameter is { Length: > 0 };

    public async Task<byte[]> OutpaintAsync(byte[] imageData, string filename,
        int padLeft, int padTop, int padRight, int padBottom,
        IProgress<string>? progress = null)
    {
        if (!HasApiKey)
            throw new InvalidOperationException(Strings.StabilityNoKey);

        if (padLeft == 0 && padTop == 0 && padRight == 0 && padBottom == 0)
            throw new InvalidOperationException(Strings.StabilityNoPadding);

        // Load image to get dimensions
        var bmp = LoadBitmap(imageData);
        var origW = bmp.PixelWidth;
        var origH = bmp.PixelHeight;

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

        // Clamp padding to API max
        padLeft = Math.Clamp(padLeft, 0, StabilityAIConfig.MaxPadding);
        padTop = Math.Clamp(padTop, 0, StabilityAIConfig.MaxPadding);
        padRight = Math.Clamp(padRight, 0, StabilityAIConfig.MaxPadding);
        padBottom = Math.Clamp(padBottom, 0, StabilityAIConfig.MaxPadding);

        // Analyze image for context-aware prompt
        progress?.Report("Analyzing image...");
        var analysis = ImageAnalyzer.Analyze(imageData);
        var prompt = ImageAnalyzer.GeneratePrompt(analysis);
        progress?.Report($"Detected: {analysis.TimeOfDay}, {analysis.Season}, {analysis.Mood}");

        progress?.Report(string.Format(Strings.StabilitySending, padLeft, padTop, padRight, padBottom));

        var boundary = $"----StabilityBoundary{Guid.NewGuid():N}";
        using var content = new MultipartFormDataContent(boundary);

        var imageContent = new ByteArrayContent(imageData);
        imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        imageContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "\"image\"",
            FileName = "\"outpaint_input.png\""
        };
        content.Add(imageContent);

        if (padLeft > 0) AddFormField(content, "left", padLeft.ToString());
        if (padTop > 0) AddFormField(content, "up", padTop.ToString());
        if (padRight > 0) AddFormField(content, "right", padRight.ToString());
        if (padBottom > 0) AddFormField(content, "down", padBottom.ToString());

        AddFormField(content, "prompt", prompt);
        AddFormField(content, "creativity", StabilityAIConfig.DefaultCreativity.ToString());
        AddFormField(content, "output_format", "png");

        progress?.Report(Strings.StabilityProcessing);

        using var request = new HttpRequestMessage(HttpMethod.Post, StabilityAIConfig.OutpaintEndpoint);
        request.Content = content;
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));

        var resp = await _http.SendAsync(request);

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

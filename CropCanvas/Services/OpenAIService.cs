using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CropCanvas.Config;
using CropCanvas.Models;
using CropCanvas.Resources;
using CropCanvas.Services.Interfaces;
using OpenAI;
using OpenAI.Images;

namespace CropCanvas.Services;

public class OpenAIService : IOutpaintProvider
{
    private string? _apiKey;

    public void SetApiKey(string? apiKey) =>
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
    public bool HasApiKey => !string.IsNullOrEmpty(_apiKey);

    public async Task<byte[]> OutpaintAsync(byte[] imageData, string filename,
        int padLeft, int padTop, int padRight, int padBottom,
        bool usePrompt = true, IProgress<string>? progress = null, AiOutpaintOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (!HasApiKey)
            throw new InvalidOperationException("OpenAI API key is not set.");

        var opt = options ?? new AiOutpaintOptions();
        var pct = AiOutpaintOptions.ClampUploadScalePercent(opt.UploadScalePercent);

        // Load source image
        var sourceBmp = LoadBitmap(imageData);
        var srcW = sourceBmp.PixelWidth;
        var srcH = sourceBmp.PixelHeight;

        // Calculate new canvas size
        var canvasW = srcW + padLeft + padRight;
        var canvasH = srcH + padTop + padBottom;

        var maxEdge = Math.Max(canvasW, canvasH);
        var allowedEdge = (int)Math.Round(maxEdge * (pct / 100.0));
        if (maxEdge > allowedEdge && pct < 100)
        {
            var s = (double)allowedEdge / maxEdge;
            progress?.Report($"Scaling canvas to {pct}% of original for OpenAI…");
            srcW = Math.Max(1, (int)(srcW * s));
            srcH = Math.Max(1, (int)(srcH * s));
            padLeft = (int)Math.Round(padLeft * s);
            padTop = (int)Math.Round(padTop * s);
            padRight = (int)Math.Round(padRight * s);
            padBottom = (int)Math.Round(padBottom * s);
            canvasW = srcW + padLeft + padRight;
            canvasH = srcH + padTop + padBottom;
            sourceBmp = ScaleBitmap(sourceBmp, srcW, srcH);
        }

        progress?.Report($"Building canvas {canvasW}x{canvasH} (pad L:{padLeft} T:{padTop} R:{padRight} B:{padBottom})...");

        // Build the composite image: original placed on larger canvas with transparent padding
        var compositeBytes = BuildCompositeImage(sourceBmp, padLeft, padTop, canvasW, canvasH);

        // Build mask: black where original is, transparent/white where AI should fill
        var maskBytes = BuildMask(srcW, srcH, padLeft, padTop, canvasW, canvasH);

        // Analyze for prompt
        string prompt;
        if (usePrompt)
        {
            progress?.Report("Analyzing image...");
            var analysis = ImageAnalyzer.Analyze(imageData);
            prompt = ImageAnalyzer.GeneratePrompt(analysis);
            progress?.Report($"Detected: {analysis.TimeOfDay}, {analysis.Season}, {analysis.Mood}");
        }
        else
        {
            prompt = "seamless natural background extension, plain, subtle, photorealistic";
        }

        progress?.Report("Sending to OpenAI...");

        var client = new OpenAIClient(_apiKey);
        var imageClient = client.GetImageClient(OpenAIConfig.DefaultModel);

        using var imageStream = new MemoryStream(compositeBytes);
        using var maskStream = new MemoryStream(maskBytes);

        var editOptions = new ImageEditOptions
        {
#pragma warning disable OPENAI001
            Size = GeneratedImageSize.Auto,
#pragma warning restore OPENAI001
            ResponseFormat = GeneratedImageFormat.Bytes
        };

        progress?.Report("Outpainting via OpenAI — generating...");

        var result = await imageClient.GenerateImageEditAsync(
            imageStream, "canvas.png",
            prompt,
            maskStream, "mask.png",
            editOptions,
            cancellationToken);

        var generatedImage = result.Value;
        var resultBytes = generatedImage.ImageBytes.ToArray();

        progress?.Report("OpenAI outpainting done!");
        return resultBytes;
    }

    /// <summary>
    /// Creates a larger canvas with the original image placed at the offset position.
    /// Padding areas are transparent (alpha = 0).
    /// </summary>
    private static byte[] BuildCompositeImage(BitmapSource source, int offsetX, int offsetY, int canvasW, int canvasH)
    {
        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            // Transparent background (default)
            ctx.DrawRectangle(System.Windows.Media.Brushes.Transparent, null,
                new Rect(0, 0, canvasW, canvasH));
            // Draw original at offset
            ctx.DrawImage(source, new Rect(offsetX, offsetY, source.PixelWidth, source.PixelHeight));
        }

        var target = new RenderTargetBitmap(canvasW, canvasH, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Creates a mask: transparent (alpha=0) where AI should generate, black where original image is.
    /// OpenAI expects: transparent areas = fill these, opaque areas = keep these.
    /// </summary>
    private static byte[] BuildMask(int srcW, int srcH, int offsetX, int offsetY, int canvasW, int canvasH)
    {
        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            // Start fully transparent (= fill everything)
            ctx.DrawRectangle(System.Windows.Media.Brushes.Transparent, null,
                new Rect(0, 0, canvasW, canvasH));
            // Draw black rectangle where original image is (= keep this area)
            ctx.DrawRectangle(System.Windows.Media.Brushes.Black, null,
                new Rect(offsetX, offsetY, srcW, srcH));
        }

        var target = new RenderTargetBitmap(canvasW, canvasH, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
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

    private static BitmapSource ScaleBitmap(BitmapSource source, int targetW, int targetH)
    {
        var scaled = new TransformedBitmap(source,
            new ScaleTransform((double)targetW / source.PixelWidth, (double)targetH / source.PixelHeight));
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(scaled));
        using var ms = new MemoryStream();
        enc.Save(ms);
        ms.Position = 0;
        return LoadBitmap(ms.ToArray());
    }
}

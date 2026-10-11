using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CropCanvas.Models;
using CropCanvas.Services.Interfaces;

namespace CropCanvas.Services;

/// <summary>
/// Outpainting via Hugging Face Space: fffiloni/diffusers-image-outpaint
/// Uses ControlNet Union + RealVisXL V5.0 Lightning + TCD Scheduler.
/// Free, cloud-based, no API key required (but subject to queue/availability).
/// </summary>
public class HuggingFaceOutpaintService : IOutpaintProvider
{
    private const string SpaceUrl = "https://fffiloni-diffusers-image-outpaint.hf.space";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };

    public async Task<byte[]> OutpaintAsync(byte[] imageData, string filename,
        int padLeft, int padTop, int padRight, int padBottom,
        bool usePrompt = true, IProgress<string>? progress = null, AiOutpaintOptions? options = null)
    {
        var opt = options ?? new AiOutpaintOptions();
        var pct = AiOutpaintOptions.ClampUploadScalePercent(opt.UploadScalePercent);
        var inferenceSteps = Math.Clamp(opt.HuggingFaceInferenceSteps, 4, 30);

        // Load image to get dimensions
        var bmp = LoadBitmap(imageData);
        var srcW = bmp.PixelWidth;
        var srcH = bmp.PixelHeight;

        var targetW = srcW + padLeft + padRight;
        var targetH = srcH + padTop + padBottom;
        var canvasLongest = Math.Max(targetW, targetH);
        var allowedLongest = (int)Math.Round(canvasLongest * (pct / 100.0));

        // Scale down only if user chose under 100% of full canvas resolution
        if (canvasLongest > allowedLongest && pct < 100)
        {
            var scale = (double)allowedLongest / canvasLongest;
            targetW = RoundTo8((int)(targetW * scale));
            targetH = RoundTo8((int)(targetH * scale));
            padLeft = (int)(padLeft * scale);
            padTop = (int)(padTop * scale);
            padRight = (int)(padRight * scale);
            padBottom = (int)(padBottom * scale);
            var newSrcW = targetW - padLeft - padRight;
            var newSrcH = targetH - padTop - padBottom;

            progress?.Report($"Scaling to {pct}% of original: {srcW}x{srcH} → {newSrcW}x{newSrcH}, target {targetW}x{targetH}");

            // Scale the source image
            imageData = ScaleAndEncode(bmp, newSrcW, newSrcH);
            srcW = newSrcW;
            srcH = newSrcH;
        }

        // Ensure target dimensions are multiples of 8
        targetW = RoundTo8(targetW);
        targetH = RoundTo8(targetH);

        var alignment = DetermineAlignment(padLeft, padTop, padRight, padBottom);
        var overlapLeft = padLeft > 0;
        var overlapRight = padRight > 0;
        var overlapTop = padTop > 0;
        var overlapBottom = padBottom > 0;
        var resizePct = CalculateResizePercentage(srcW, srcH, targetW, targetH);

        string prompt = "";
        if (usePrompt)
        {
            progress?.Report("Analyzing image...");
            var analysis = ImageAnalyzer.Analyze(imageData);
            prompt = ImageAnalyzer.GeneratePrompt(analysis);
            progress?.Report($"Detected: {analysis.TimeOfDay}, {analysis.Season}, {analysis.Mood}");
        }

        // Step 1: Upload
        progress?.Report($"Uploading to HuggingFace ({imageData.Length / 1024}KB)...");
        var uploadedPath = await UploadImageAsync(imageData, filename);

        // Step 2: Call /infer
        progress?.Report($"Outpainting via HuggingFace ({targetW}x{targetH}, {alignment}, resize {resizePct:F0}%)...");

        var payload = new
        {
            data = new object[]
            {
                new { path = uploadedPath, orig_name = filename, is_stream = false, meta = new {} },
                (double)targetW,
                (double)targetH,
                10.0,       // overlap_percentage
                (double)inferenceSteps,
                "Custom",
                resizePct,
                prompt,
                alignment,
                overlapLeft,
                overlapRight,
                overlapTop,
                overlapBottom
            }
        };

        var json = JsonSerializer.Serialize(payload);
        var callResp = await _http.PostAsync($"{SpaceUrl}/gradio_api/call/infer",
            new StringContent(json, Encoding.UTF8, "application/json"));

        if (!callResp.IsSuccessStatusCode)
        {
            var errBody = await callResp.Content.ReadAsStringAsync();
            throw new Exception($"HuggingFace API error ({callResp.StatusCode}): {errBody}");
        }

        var callResult = await callResp.Content.ReadAsStringAsync();
        using var callDoc = JsonDocument.Parse(callResult);

        if (!callDoc.RootElement.TryGetProperty("event_id", out var eventIdProp) || eventIdProp.GetString() is not string eventId)
            throw new Exception($"HuggingFace did not return an event_id. Response: {callResult}");

        // Step 3: Poll for result via SSE stream
        progress?.Report("Processing on HuggingFace GPU (this may take 30-60s)...");
        return await PollForResultAsync(eventId, progress);
    }

    private async Task<string> UploadImageAsync(byte[] imageData, string filename)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(imageData);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "files", filename);

        var resp = await _http.PostAsync($"{SpaceUrl}/gradio_api/upload", content);
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(result);
        return doc.RootElement[0].GetString()!;
    }

    private async Task<byte[]> PollForResultAsync(string eventId, IProgress<string>? progress)
    {
        using var resp = await _http.GetStreamAsync($"{SpaceUrl}/gradio_api/call/infer/{eventId}");
        using var reader = new StreamReader(resp);

        string? lastImageUrl = null;
        int stepCount = 0;

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (line == null) continue;

            if (line.StartsWith("event: "))
            {
                var eventType = line[7..];
                var dataLine = await reader.ReadLineAsync();

                if (eventType == "error")
                {
                    var errorMsg = "Unknown error";
                    if (dataLine?.StartsWith("data: ") == true)
                    {
                        try
                        {
                            using var errDoc = JsonDocument.Parse(dataLine[6..]);
                            errorMsg = errDoc.RootElement.ToString();
                        }
                        catch { errorMsg = dataLine[6..]; }
                    }
                    throw new Exception($"HuggingFace error: {errorMsg}");
                }

                if ((eventType == "generating" || eventType == "complete") && dataLine?.StartsWith("data: ") == true)
                {
                    stepCount++;
                    progress?.Report($"Generating... step {stepCount}");

                    // Parse the result: [[cnet_image, final_image]]
                    try
                    {
                        var data = dataLine[6..];
                        using var doc = JsonDocument.Parse(data);
                        var root = doc.RootElement;

                        // Navigate into nested arrays: [[obj1, obj2]] or [obj1, obj2]
                        JsonElement imageArray = root;
                        if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0
                            && root[0].ValueKind == JsonValueKind.Array)
                            imageArray = root[0]; // unwrap outer array

                        if (imageArray.ValueKind == JsonValueKind.Array && imageArray.GetArrayLength() >= 2)
                        {
                            var finalImage = imageArray[1]; // second element = final result
                            if (finalImage.TryGetProperty("url", out var urlProp) && urlProp.GetString() is string url)
                                lastImageUrl = url;
                            else if (finalImage.TryGetProperty("path", out var pathProp) && pathProp.GetString() is string path)
                                lastImageUrl = $"{SpaceUrl}/gradio_api/file={path}";
                        }
                    }
                    catch { /* skip unparseable intermediate results */ }

                    if (eventType == "complete" && lastImageUrl != null)
                    {
                        progress?.Report("Downloading result...");
                        return await _http.GetByteArrayAsync(lastImageUrl);
                    }
                }
            }
        }

        // If stream ended without "complete" but we have a last image, use it
        if (lastImageUrl != null)
        {
            progress?.Report("Downloading final result...");
            return await _http.GetByteArrayAsync(lastImageUrl);
        }

        throw new TimeoutException("HuggingFace did not return a result.");
    }

    private static string DetermineAlignment(int padL, int padT, int padR, int padB)
    {
        // Determine where the original image should be placed on the canvas
        if (padL > padR * 2) return "Right";   // More padding on left → image goes right
        if (padR > padL * 2) return "Left";     // More padding on right → image goes left
        if (padT > padB * 2) return "Bottom";
        if (padB > padT * 2) return "Top";
        return "Middle";
    }

    private static double CalculateResizePercentage(int srcW, int srcH, int targetW, int targetH)
    {
        // How much of the target canvas does the source fill?
        var wRatio = (double)srcW / targetW * 100;
        var hRatio = (double)srcH / targetH * 100;
        return Math.Min(wRatio, hRatio);
    }

    private static int RoundTo8(int v) => Math.Max(64, ((v + 7) / 8) * 8);

    private static byte[] ScaleAndEncode(System.Windows.Media.Imaging.BitmapSource source, int w, int h)
    {
        var scaleX = (double)w / source.PixelWidth;
        var scaleY = (double)h / source.PixelHeight;
        var scaled = new System.Windows.Media.Imaging.TransformedBitmap(source,
            new System.Windows.Media.ScaleTransform(scaleX, scaleY));
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(scaled));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static System.Windows.Media.Imaging.BitmapSource LoadBitmap(byte[] data)
    {
        var bmp = new System.Windows.Media.Imaging.BitmapImage();
        bmp.BeginInit();
        bmp.StreamSource = new MemoryStream(data);
        bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }
}

using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CropCanvas.Config;
using CropCanvas.Models;
using CropCanvas.Resources;
using CropCanvas.Services.Interfaces;

namespace CropCanvas.Services;

public class ComfyUIService : IOutpaintProvider
{
    private string _baseUrl = ComfyUIConfig.DefaultBaseUrl;
    private HttpClient _http = new() { BaseAddress = new Uri(ComfyUIConfig.DefaultBaseUrl) };
    private readonly string _clientId = Guid.NewGuid().ToString("N")[..8];

    /// <summary>Sets the ComfyUI server base URL (http/https, no path). Recreates the HTTP client.</summary>
    public void SetBaseUrl(string? baseUrl)
    {
        var normalized = NormalizeBaseUrl(string.IsNullOrWhiteSpace(baseUrl)
            ? ComfyUIConfig.DefaultBaseUrl
            : baseUrl.Trim());
        if (normalized == _baseUrl)
            return;

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            // Incomplete while typing — keep previous client; store text for later validation.
            _baseUrl = normalized;
            return;
        }

        _baseUrl = normalized;
        // Do not Dispose the previous client here — in-flight requests may still use it.
        _http = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromMinutes(ComfyUIConfig.TimeoutMinutes) };
    }

    private static string NormalizeBaseUrl(string url)
    {
        url = url.Trim();
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            url = "http://" + url.TrimStart('/');
        return url.TrimEnd('/');
    }

    private void EnsureValidBaseUrl()
    {
        if (!Uri.TryCreate(_baseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrEmpty(uri.Host))
            throw new ArgumentException($"Invalid ComfyUI base URL: {_baseUrl}");
    }

    private Uri BuildWebSocketUri()
    {
        var u = new Uri(_baseUrl);
        var wsScheme = string.Equals(u.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            ? "wss"
            : "ws";
        return new UriBuilder
        {
            Scheme = wsScheme,
            Host = u.Host,
            Port = u.Port,
            Path = "/ws",
            Query = "clientId=" + Uri.EscapeDataString(_clientId)
        }.Uri;
    }

    public async Task<byte[]> OutpaintAsync(byte[] imageData, string filename,
        int padLeft, int padTop, int padRight, int padBottom,
        bool usePrompt = true, IProgress<string>? progress = null, AiOutpaintOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var opt = options ?? new AiOutpaintOptions();
        var pct = AiOutpaintOptions.ClampUploadScalePercent(opt.UploadScalePercent);
        var jpegQ = Math.Clamp(opt.UploadJpegQuality, 70, 100);
        var samplerSteps = opt.ComfySamplerSteps;

        EnsureValidBaseUrl();
        progress?.Report(Strings.StatusConnecting);
        if (!await CheckConnectionAsync(cancellationToken))
            throw new Exception(string.Format(Strings.StatusComfyNotFound, _baseUrl));

        var checkpoints = await GetAvailableCheckpointsAsync(cancellationToken);
        if (checkpoints.Count == 0)
            throw new Exception(Strings.StatusNoModel);

        // IMPORTANT: Outpainting requires an inpainting-specific model, not a base model
        // Prefer: SDXL inpainting (RealVisXL) > any inpainting > Lightning (fallback) > first
        var checkpoint = checkpoints.FirstOrDefault(c =>
                c.Contains("inpaint", StringComparison.OrdinalIgnoreCase) &&
                (c.Contains("XL", StringComparison.OrdinalIgnoreCase) ||
                 c.Contains("realvis", StringComparison.OrdinalIgnoreCase)))
            ?? checkpoints.FirstOrDefault(c => c.Contains("inpaint", StringComparison.OrdinalIgnoreCase))
            ?? checkpoints[0];
        progress?.Report($"Using model: {checkpoint}");

        // Optional downscale: longest edge × (upload % / 100). At 100% no user downscale.
        var bmp = new System.Windows.Media.Imaging.BitmapImage();
        bmp.BeginInit();
        bmp.StreamSource = new System.IO.MemoryStream(imageData);
        bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        var srcW = bmp.PixelWidth;
        var srcH = bmp.PixelHeight;
        var longest = Math.Max(srcW, srcH);
        var targetLongest = (int)Math.Round(longest * (pct / 100.0));
        if (longest > targetLongest && pct < 100)
        {
            var scale = (double)targetLongest / longest;
            var newW = (int)(srcW * scale);
            var newH = (int)(srcH * scale);
            padLeft = (int)(padLeft * scale);
            padTop = (int)(padTop * scale);
            padRight = (int)(padRight * scale);
            padBottom = (int)(padBottom * scale);

            progress?.Report($"Scaling {srcW}x{srcH} → {newW}x{newH} ({pct}% of original for AI upload)");

            var scaled = new System.Windows.Media.Imaging.TransformedBitmap(bmp,
                new System.Windows.Media.ScaleTransform(scale, scale));
            var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder { QualityLevel = jpegQ };
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(scaled));
            using var ms = new System.IO.MemoryStream();
            encoder.Save(ms);
            imageData = ms.ToArray();
        }

        string? positivePrompt = null;
        string? negativePrompt = null;

        if (usePrompt)
        {
            progress?.Report("Analyzing image...");
            var analysis = await Task.Run(() => ImageAnalyzer.Analyze(imageData), cancellationToken);
            positivePrompt = ImageAnalyzer.GeneratePrompt(analysis);
            negativePrompt = ImageAnalyzer.GenerateNegativePrompt();
            progress?.Report($"Detected: {analysis.TimeOfDay}, {analysis.Season}, {analysis.Mood}");
        }
        else
        {
            progress?.Report("Skipping image analysis — AI will decide.");
        }

        progress?.Report(Strings.StatusUploading);
        var uploadedName = await UploadImageAsync(imageData, filename, cancellationToken);

        progress?.Report(string.Format(Strings.StatusOutpainting, padLeft, padTop, padRight, padBottom));
        var promptId = await QueueOutpaintAsync(uploadedName, padLeft, padTop, padRight, padBottom,
            checkpoint, positivePrompt, negativePrompt, samplerSteps, progress, cancellationToken);

        return await WaitAndDownloadResultAsync(promptId, progress, cancellationToken);
    }

    public async Task<bool> CheckConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var resp = await _http.GetAsync("/system_stats", cancellationToken);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<List<string>> GetAvailableCheckpointsAsync(CancellationToken cancellationToken = default)
    {
        var resp = await _http.GetStringAsync("/object_info/CheckpointLoaderSimple", cancellationToken);
        using var doc = JsonDocument.Parse(resp);
        var ckpts = doc.RootElement
            .GetProperty("CheckpointLoaderSimple")
            .GetProperty("input")
            .GetProperty("required")
            .GetProperty("ckpt_name")[0];

        var result = new List<string>();
        foreach (var item in ckpts.EnumerateArray())
            result.Add(item.GetString()!);
        return result;
    }

    public async Task<bool> HasControlNetUnionAsync()
    {
        try
        {
            var resp = await _http.GetStringAsync("/object_info/ControlNetLoader");
            using var doc = JsonDocument.Parse(resp);
            var models = doc.RootElement
                .GetProperty("ControlNetLoader")
                .GetProperty("input")
                .GetProperty("required")
                .GetProperty("control_net_name")[0];

            foreach (var m in models.EnumerateArray())
                if (m.GetString()?.Contains("union", StringComparison.OrdinalIgnoreCase) == true
                    && m.GetString()?.Contains("promax", StringComparison.OrdinalIgnoreCase) == true)
                    return true;
            return false;
        }
        catch { return false; }
    }

    public async Task<string?> GetControlNetModelNameAsync()
    {
        try
        {
            var resp = await _http.GetStringAsync("/object_info/ControlNetLoader");
            using var doc = JsonDocument.Parse(resp);
            var models = doc.RootElement
                .GetProperty("ControlNetLoader")
                .GetProperty("input")
                .GetProperty("required")
                .GetProperty("control_net_name")[0];

            foreach (var m in models.EnumerateArray())
            {
                var name = m.GetString();
                if (name?.Contains("union", StringComparison.OrdinalIgnoreCase) == true
                    && name.Contains("promax", StringComparison.OrdinalIgnoreCase) == true)
                    return name;
            }
            return null;
        }
        catch { return null; }
    }

    public async Task<string> UploadImageAsync(byte[] imageData, string filename, CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(imageData), "image", filename);
        content.Add(new StringContent("input"), "type");
        content.Add(new StringContent("true"), "overwrite");

        var resp = await _http.PostAsync("/upload/image", content, cancellationToken);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("name").GetString()!;
    }

    public async Task<string> QueueOutpaintAsync(string uploadedImageName, int padLeft, int padTop,
        int padRight, int padBottom, string checkpoint,
        string? positivePrompt = null, string? negativePrompt = null,
        int samplerSteps = 30,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        padLeft = RoundUp8(padLeft);
        padTop = RoundUp8(padTop);
        padRight = RoundUp8(padRight);
        padBottom = RoundUp8(padBottom);

        progress?.Report(Strings.ComfyPreparing);

        // TODO: ControlNet Union workflow needs debugging — using simple workflow for now
        var controlNetName = await GetControlNetModelNameAsync();
        Dictionary<string, object> workflow;

        if (false && controlNetName != null) // Disabled until ControlNet workflow is fixed
        {
            progress?.Report($"Using ControlNet Union ({controlNetName})");
            workflow = ComfyUIWorkflowBuilder.Build(uploadedImageName, padLeft, padTop, padRight, padBottom,
                checkpoint, positivePrompt, negativePrompt);
        }
        else
        {
            progress?.Report("ControlNet Union not found — using basic workflow");
            workflow = ComfyUIWorkflowBuilder.BuildSimple(uploadedImageName, padLeft, padTop, padRight, padBottom,
                checkpoint, positivePrompt, negativePrompt, samplerSteps);
        }
        var payload = JsonSerializer.Serialize(new { prompt = workflow, client_id = _clientId });

        var resp = await _http.PostAsync("/prompt",
            new StringContent(payload, Encoding.UTF8, "application/json"), cancellationToken);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("prompt_id").GetString()!;
    }

    public async Task<byte[]> WaitAndDownloadResultAsync(string promptId, IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Try WebSocket first for real-time progress, fallback to polling
        try
        {
            using var ws = new ClientWebSocket();
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(ComfyUIConfig.TimeoutMinutes));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            var token = linked.Token;

            await ws.ConnectAsync(BuildWebSocketUri(), token);
            progress?.Report(Strings.ComfyConnected);

            var buffer = new byte[ComfyUIConfig.WebSocketBufferSize];
            while (ws.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await ws.ReceiveAsync(buffer, token);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    throw new TimeoutException(Strings.ComfyTimeout);
                }

                // Skip binary messages (preview images)
                if (result.MessageType != WebSocketMessageType.Text)
                    continue;

                // Handle fragmented messages
                var msgBuilder = new StringBuilder(Encoding.UTF8.GetString(buffer, 0, result.Count));
                while (!result.EndOfMessage)
                {
                    result = await ws.ReceiveAsync(buffer, token);
                    msgBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                }

                var msg = msgBuilder.ToString();
                JsonElement data = default;
                string? type = null;

                try
                {
                    using var doc = JsonDocument.Parse(msg);
                    type = doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : null;
                    data = doc.RootElement.TryGetProperty("data", out var d) ? d.Clone() : default;
                }
                catch { continue; } // Skip unparseable messages

                switch (type)
                {
                    case "execution_start":
                        progress?.Report(Strings.ComfyModelLoading);
                        break;

                    case "progress":
                        var step = data.TryGetProperty("value", out var v) ? v.GetInt32() : 0;
                        var maxSteps = data.TryGetProperty("max", out var m) ? m.GetInt32() : 0;
                        if (maxSteps > 0)
                            progress?.Report(string.Format(Strings.ComfyGenerating, step, maxSteps, 100 * step / maxSteps));
                        break;

                    case "executing":
                        var nodeId = data.TryGetProperty("node", out var n) ? n.GetString() : null;
                        if (nodeId == null)
                        {
                            progress?.Report(Strings.ComfyProcessingDone);
                            await Task.Delay(500, cancellationToken);
                            return await DownloadResultFromHistory(promptId, progress, cancellationToken);
                        }
                        break;

                    case "execution_error":
                        var errorMsg = data.TryGetProperty("exception_message", out var em) ? em.GetString() : "Unknown error";
                        throw new Exception(string.Format(Strings.ComfyError, errorMsg));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (WebSocketException)
        {
            progress?.Report(Strings.ComfyWsFallback);
        }
        catch (TimeoutException) { throw; }
        catch (Exception ex) when (ex.Message.StartsWith("ComfyUI")) { throw; }
        catch
        {
            progress?.Report(Strings.ComfyWsError);
        }

        // Fallback to polling
        return await PollAndDownloadResultAsync(promptId, progress, cancellationToken);
    }

    private async Task<byte[]> PollAndDownloadResultAsync(string promptId, IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        for (int i = 0; i < ComfyUIConfig.MaxPollAttempts; i++)
        {
            await Task.Delay(ComfyUIConfig.PollIntervalMs, cancellationToken);
            progress?.Report(string.Format(Strings.ComfyPollProgress, i + 1));

            var bytes = await TryDownloadFromHistory(promptId, cancellationToken);
            if (bytes != null)
                return bytes;
        }
        throw new TimeoutException(Strings.ComfyPollTimeout);
    }

    private async Task<byte[]> DownloadResultFromHistory(string promptId, IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        for (int i = 0; i < 10; i++)
        {
            var bytes = await TryDownloadFromHistory(promptId, cancellationToken);
            if (bytes != null)
            {
                progress?.Report(Strings.ComfyResultDownloaded);
                return bytes;
            }
            await Task.Delay(500, cancellationToken);
        }
        throw new Exception(Strings.ComfyResultFailed);
    }

    private async Task<byte[]?> TryDownloadFromHistory(string promptId, CancellationToken cancellationToken = default)
    {
        var resp = await _http.GetStringAsync($"/history/{promptId}", cancellationToken);
        using var doc = JsonDocument.Parse(resp);

        if (!doc.RootElement.TryGetProperty(promptId, out var entry)) return null;
        if (!entry.TryGetProperty("outputs", out var outputs)) return null;

        foreach (var node in outputs.EnumerateObject())
        {
            if (!node.Value.TryGetProperty("images", out var images)) continue;
            foreach (var img in images.EnumerateArray())
            {
                var filename = img.GetProperty("filename").GetString()!;
                var subfolder = img.TryGetProperty("subfolder", out var sf) ? sf.GetString() ?? "" : "";
                var type = img.TryGetProperty("type", out var tp) ? tp.GetString() ?? "output" : "output";

                return await _http.GetByteArrayAsync(
                    $"/view?filename={Uri.EscapeDataString(filename)}&subfolder={Uri.EscapeDataString(subfolder)}&type={Uri.EscapeDataString(type)}",
                    cancellationToken);
            }
        }
        return null;
    }

    private static int RoundUp8(int value) => ((value + 7) / 8) * 8;
}

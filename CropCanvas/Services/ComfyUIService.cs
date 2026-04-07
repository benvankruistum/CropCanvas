using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CropCanvas.Config;
using CropCanvas.Resources;
using CropCanvas.Services.Interfaces;

namespace CropCanvas.Services;

public class ComfyUIService : IOutpaintProvider
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri(ComfyUIConfig.BaseUrl) };
    private readonly string _clientId = Guid.NewGuid().ToString("N")[..8];

    public async Task<byte[]> OutpaintAsync(byte[] imageData, string filename,
        int padLeft, int padTop, int padRight, int padBottom,
        IProgress<string>? progress = null)
    {
        progress?.Report(Strings.StatusConnecting);
        if (!await CheckConnectionAsync())
            throw new Exception(Strings.StatusComfyNotFound);

        var checkpoints = await GetAvailableCheckpointsAsync();
        if (checkpoints.Count == 0)
            throw new Exception(Strings.StatusNoModel);

        // Prefer SDXL inpainting > any inpainting > first available
        var checkpoint = checkpoints.FirstOrDefault(c =>
                c.Contains("inpaint", StringComparison.OrdinalIgnoreCase) &&
                (c.Contains("XL", StringComparison.OrdinalIgnoreCase) ||
                 c.Contains("realvis", StringComparison.OrdinalIgnoreCase) ||
                 c.Contains("sdxl", StringComparison.OrdinalIgnoreCase)))
            ?? checkpoints.FirstOrDefault(c => c.Contains("inpaint", StringComparison.OrdinalIgnoreCase))
            ?? checkpoints[0];

        // Analyze image for context-aware prompt
        progress?.Report("Analyzing image...");
        var analysis = await Task.Run(() => ImageAnalyzer.Analyze(imageData));
        var positivePrompt = ImageAnalyzer.GeneratePrompt(analysis);
        var negativePrompt = ImageAnalyzer.GenerateNegativePrompt();
        progress?.Report($"Detected: {analysis.TimeOfDay}, {analysis.Season}, {analysis.Mood}");

        progress?.Report(Strings.StatusUploading);
        var uploadedName = await UploadImageAsync(imageData, filename);

        progress?.Report(string.Format(Strings.StatusOutpainting, padLeft, padTop, padRight, padBottom));
        var promptId = await QueueOutpaintAsync(uploadedName, padLeft, padTop, padRight, padBottom,
            checkpoint, positivePrompt, negativePrompt, progress);

        return await WaitAndDownloadResultAsync(promptId, progress);
    }

    public async Task<bool> CheckConnectionAsync()
    {
        try
        {
            var resp = await _http.GetAsync("/system_stats");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<List<string>> GetAvailableCheckpointsAsync()
    {
        var resp = await _http.GetStringAsync("/object_info/CheckpointLoaderSimple");
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

    public async Task<string> UploadImageAsync(byte[] imageData, string filename)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(imageData), "image", filename);
        content.Add(new StringContent("input"), "type");
        content.Add(new StringContent("true"), "overwrite");

        var resp = await _http.PostAsync("/upload/image", content);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("name").GetString()!;
    }

    public async Task<string> QueueOutpaintAsync(string uploadedImageName, int padLeft, int padTop,
        int padRight, int padBottom, string checkpoint,
        string? positivePrompt = null, string? negativePrompt = null,
        IProgress<string>? progress = null)
    {
        padLeft = RoundUp8(padLeft);
        padTop = RoundUp8(padTop);
        padRight = RoundUp8(padRight);
        padBottom = RoundUp8(padBottom);

        progress?.Report(Strings.ComfyPreparing);

        var workflow = ComfyUIWorkflowBuilder.Build(uploadedImageName, padLeft, padTop, padRight, padBottom,
            checkpoint, positivePrompt, negativePrompt);
        var payload = JsonSerializer.Serialize(new { prompt = workflow, client_id = _clientId });

        var resp = await _http.PostAsync("/prompt",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("prompt_id").GetString()!;
    }

    public async Task<byte[]> WaitAndDownloadResultAsync(string promptId, IProgress<string>? progress = null)
    {
        // Try WebSocket first for real-time progress, fallback to polling
        try
        {
            using var ws = new ClientWebSocket();
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(ComfyUIConfig.TimeoutMinutes));

            await ws.ConnectAsync(new Uri($"ws://{ComfyUIConfig.BaseUrl.Replace("http://", "")}/ws?clientId={_clientId}"), cts.Token);
            progress?.Report(Strings.ComfyConnected);

            var buffer = new byte[ComfyUIConfig.WebSocketBufferSize];
            while (ws.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await ws.ReceiveAsync(buffer, cts.Token);
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
                    result = await ws.ReceiveAsync(buffer, cts.Token);
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
                            await Task.Delay(500);
                            return await DownloadResultFromHistory(promptId, progress);
                        }
                        break;

                    case "execution_error":
                        var errorMsg = data.TryGetProperty("exception_message", out var em) ? em.GetString() : "Unknown error";
                        throw new Exception(string.Format(Strings.ComfyError, errorMsg));
                }
            }
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
        return await PollAndDownloadResultAsync(promptId, progress);
    }

    private async Task<byte[]> PollAndDownloadResultAsync(string promptId, IProgress<string>? progress = null)
    {
        for (int i = 0; i < ComfyUIConfig.MaxPollAttempts; i++)
        {
            await Task.Delay(ComfyUIConfig.PollIntervalMs);
            progress?.Report(string.Format(Strings.ComfyPollProgress, i + 1));

            var bytes = await TryDownloadFromHistory(promptId);
            if (bytes != null)
                return bytes;
        }
        throw new TimeoutException(Strings.ComfyPollTimeout);
    }

    private async Task<byte[]> DownloadResultFromHistory(string promptId, IProgress<string>? progress = null)
    {
        for (int i = 0; i < 10; i++)
        {
            var bytes = await TryDownloadFromHistory(promptId);
            if (bytes != null)
            {
                progress?.Report(Strings.ComfyResultDownloaded);
                return bytes;
            }
            await Task.Delay(500);
        }
        throw new Exception(Strings.ComfyResultFailed);
    }

    private async Task<byte[]?> TryDownloadFromHistory(string promptId)
    {
        var resp = await _http.GetStringAsync($"/history/{promptId}");
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
                    $"/view?filename={Uri.EscapeDataString(filename)}&subfolder={Uri.EscapeDataString(subfolder)}&type={Uri.EscapeDataString(type)}");
            }
        }
        return null;
    }

    private static int RoundUp8(int value) => ((value + 7) / 8) * 8;
}

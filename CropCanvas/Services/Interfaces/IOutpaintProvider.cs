using CropCanvas.Models;

namespace CropCanvas.Services.Interfaces;

public interface IOutpaintProvider
{
    Task<byte[]> OutpaintAsync(byte[] imageData, string filename,
        int padLeft, int padTop, int padRight, int padBottom,
        bool usePrompt = true, IProgress<string>? progress = null,
        AiOutpaintOptions? options = null);
}

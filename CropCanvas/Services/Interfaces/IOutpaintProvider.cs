namespace CropCanvas.Services.Interfaces;

public interface IOutpaintProvider
{
    Task<byte[]> OutpaintAsync(byte[] imageData, string filename, int padLeft, int padTop, int padRight, int padBottom, IProgress<string>? progress = null);
}

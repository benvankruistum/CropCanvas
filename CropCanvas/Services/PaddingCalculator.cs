namespace CropCanvas.Services;

public static class PaddingCalculator
{
    public record PaddingResult(int Left, int Top, int Right, int Bottom);

    public static PaddingResult Calculate(double normX, double normY, double normW, double normH, int origW, int origH)
    {
        var padLeft = Math.Max(0, (int)Math.Round(-normX * origW));
        var padTop = Math.Max(0, (int)Math.Round(-normY * origH));
        var padRight = Math.Max(0, (int)Math.Round((normX + normW - 1.0) * origW));
        var padBottom = Math.Max(0, (int)Math.Round((normY + normH - 1.0) * origH));
        return new(padLeft, padTop, padRight, padBottom);
    }

    public static bool HasOutOfBounds(PaddingResult p) => p.Left > 0 || p.Top > 0 || p.Right > 0 || p.Bottom > 0;
}

using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CropCanvas.Services;

/// <summary>
/// Analyzes image properties (lighting, color palette, mood) to generate
/// a context-aware prompt for AI outpainting.
/// </summary>
public static class ImageAnalyzer
{
    public record ImageAnalysis(
        string TimeOfDay,
        string Season,
        string Mood,
        string ColorPalette,
        string LightingDirection,
        double AverageBrightness,
        double WarmthScore);

    /// <summary>
    /// Analyzes a region of the image (the visible portion near the edge being extended)
    /// and returns descriptive properties for prompt generation.
    /// </summary>
    public static ImageAnalysis Analyze(byte[] imageData)
    {
        var bmp = LoadBitmap(imageData);

        // Sample pixels from the image
        var pixels = SamplePixels(bmp, 200);

        var avgR = pixels.Average(p => p.R);
        var avgG = pixels.Average(p => p.G);
        var avgB = pixels.Average(p => p.B);
        var brightness = (avgR * 0.299 + avgG * 0.587 + avgB * 0.114) / 255.0;

        // Color temperature: warm (orange/yellow) vs cool (blue)
        var warmth = (avgR - avgB) / 255.0; // -1 = very cool, +1 = very warm

        // Analyze brightness distribution for time of day
        var brightPixels = pixels.Count(p => Luminance(p) > 0.7);
        var darkPixels = pixels.Count(p => Luminance(p) < 0.3);
        var brightRatio = (double)brightPixels / pixels.Count;
        var darkRatio = (double)darkPixels / pixels.Count;

        // Analyze edge brightness for lighting direction
        var leftPixels = SampleEdge(bmp, Edge.Left, 50);
        var rightPixels = SampleEdge(bmp, Edge.Right, 50);
        var topPixels = SampleEdge(bmp, Edge.Top, 50);
        var bottomPixels = SampleEdge(bmp, Edge.Bottom, 50);

        var leftBright = leftPixels.Average(Luminance);
        var rightBright = rightPixels.Average(Luminance);
        var topBright = topPixels.Average(Luminance);
        var bottomBright = bottomPixels.Average(Luminance);

        // Determine properties
        var timeOfDay = DetermineTimeOfDay(brightness, warmth, brightRatio, darkRatio);
        var season = DetermineSeason(avgR, avgG, avgB, warmth, brightness);
        var mood = DetermineMood(brightness, warmth);
        var palette = DescribeColorPalette(avgR, avgG, avgB, warmth);
        var lightDir = DescribeLighting(leftBright, rightBright, topBright, bottomBright, brightness);

        return new ImageAnalysis(timeOfDay, season, mood, palette, lightDir, brightness, warmth);
    }

    /// <summary>
    /// Generates an outpainting prompt based on image analysis.
    /// </summary>
    public static string GeneratePrompt(ImageAnalysis analysis)
    {
        var parts = new List<string>
        {
            "photorealistic",
            "natural seamless continuation",
            $"{analysis.TimeOfDay} lighting",
            $"{analysis.Mood} atmosphere",
            $"{analysis.ColorPalette}",
            $"{analysis.LightingDirection}",
            $"{analysis.Season} scene",
            "consistent texture and detail",
            "matching perspective and depth of field",
            "8k, sharp focus"
        };

        return string.Join(", ", parts);
    }

    public static string GenerateNegativePrompt()
    {
        return "visible seam, border, different lighting, different season, different time of day, " +
               "color mismatch, brightness mismatch, blurry, artifacts, low quality, watermark, " +
               "text, logo, distorted, deformed, duplicate, out of focus, " +
               "sudden change in vegetation, inconsistent shadows";
    }

    private static string DetermineTimeOfDay(double brightness, double warmth, double brightRatio, double darkRatio)
    {
        if (brightness < 0.15) return "nighttime";
        if (brightness < 0.3 && warmth > 0.05) return "golden hour sunset";
        if (brightness < 0.3 && warmth < -0.05) return "blue hour twilight";
        if (brightness < 0.35) return "dawn or dusk";
        if (warmth > 0.15 && brightness > 0.5) return "warm golden hour";
        if (warmth > 0.1 && brightness < 0.5) return "late afternoon warm";
        if (brightness > 0.7 && warmth < 0.05) return "bright overcast daylight";
        if (brightness > 0.6) return "midday daylight";
        return "soft daylight";
    }

    private static string DetermineSeason(double r, double g, double b, double warmth, double brightness)
    {
        // High green dominance = summer/spring
        if (g > r * 1.1 && g > b * 1.3) return "lush green summer";
        // Warm orange/brown tones = autumn
        if (r > g * 1.1 && warmth > 0.1 && g > b) return "autumn";
        // Very bright, cool = winter/snow
        if (brightness > 0.7 && warmth < 0.0) return "winter";
        // Muted greens with some warmth = spring
        if (g > b && warmth > 0.0 && brightness > 0.4) return "spring";
        // Brownish, low saturation = late autumn or winter
        if (warmth > 0.05 && brightness < 0.4) return "late autumn";
        return "natural outdoor";
    }

    private static string DetermineMood(double brightness, double warmth)
    {
        if (brightness < 0.2) return "dark and moody";
        if (brightness < 0.35 && warmth > 0.05) return "warm and atmospheric";
        if (brightness < 0.35) return "misty and mysterious";
        if (warmth > 0.15) return "warm and inviting";
        if (brightness > 0.7) return "bright and airy";
        if (warmth < -0.05) return "cool and serene";
        return "natural and balanced";
    }

    private static string DescribeColorPalette(double r, double g, double b, double warmth)
    {
        var parts = new List<string>();

        if (warmth > 0.1) parts.Add("warm tones");
        else if (warmth < -0.1) parts.Add("cool blue tones");
        else parts.Add("neutral tones");

        if (g > r && g > b) parts.Add("earthy greens");
        if (r > g * 1.2) parts.Add("warm amber");
        if (b > r && b > g) parts.Add("blue sky tones");

        return string.Join(" with ", parts);
    }

    private static string DescribeLighting(double left, double right, double top, double bottom, double overall)
    {
        var maxEdge = Math.Max(Math.Max(left, right), Math.Max(top, bottom));
        var minEdge = Math.Min(Math.Min(left, right), Math.Min(top, bottom));

        if (maxEdge - minEdge < 0.1) return "even diffused lighting";

        if (top > bottom * 1.3 && top == maxEdge) return "overhead lighting from above";
        if (left > right * 1.2) return "side lighting from the left";
        if (right > left * 1.2) return "side lighting from the right";
        if (bottom > top * 1.2) return "low angle lighting from below";

        return "soft directional lighting";
    }

    private static double Luminance(Color c) => (c.R * 0.299 + c.G * 0.587 + c.B * 0.114) / 255.0;

    enum Edge { Left, Right, Top, Bottom }

    private static List<Color> SamplePixels(BitmapSource bmp, int count)
    {
        var pixels = new List<Color>();
        var w = bmp.PixelWidth;
        var h = bmp.PixelHeight;
        var stride = w * 4;
        var data = new byte[stride * h];
        bmp.CopyPixels(data, stride, 0);

        var rng = new Random(42);
        for (int i = 0; i < count; i++)
        {
            var x = rng.Next(w);
            var y = rng.Next(h);
            var idx = (y * stride) + (x * 4);
            if (idx + 2 < data.Length)
                pixels.Add(Color.FromRgb(data[idx + 2], data[idx + 1], data[idx])); // BGRA
        }
        return pixels;
    }

    private static List<Color> SampleEdge(BitmapSource bmp, Edge edge, int count)
    {
        var pixels = new List<Color>();
        var w = bmp.PixelWidth;
        var h = bmp.PixelHeight;
        var stride = w * 4;
        var data = new byte[stride * h];
        bmp.CopyPixels(data, stride, 0);

        var rng = new Random(123);
        var margin = Math.Min(w, h) / 10; // 10% from edge

        for (int i = 0; i < count; i++)
        {
            int x, y;
            switch (edge)
            {
                case Edge.Left: x = rng.Next(margin); y = rng.Next(h); break;
                case Edge.Right: x = w - 1 - rng.Next(margin); y = rng.Next(h); break;
                case Edge.Top: x = rng.Next(w); y = rng.Next(margin); break;
                case Edge.Bottom: x = rng.Next(w); y = h - 1 - rng.Next(margin); break;
                default: x = 0; y = 0; break;
            }

            x = Math.Clamp(x, 0, w - 1);
            y = Math.Clamp(y, 0, h - 1);
            var idx = (y * stride) + (x * 4);
            if (idx + 2 < data.Length)
                pixels.Add(Color.FromRgb(data[idx + 2], data[idx + 1], data[idx]));
        }
        return pixels;
    }

    private static BitmapSource LoadBitmap(byte[] data)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.StreamSource = new MemoryStream(data);
        bmp.DecodePixelWidth = 512; // Small decode for fast analysis
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }
}

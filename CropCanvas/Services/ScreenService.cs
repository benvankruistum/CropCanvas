namespace CropCanvas.Services;

public class ScreenService
{
    public (int Width, int Height) GetScreenResolution()
    {
        var screen = System.Windows.Forms.Screen.PrimaryScreen;
        if (screen == null)
            return (1920, 1080);

        return (screen.Bounds.Width, screen.Bounds.Height);
    }

    public (int RatioW, int RatioH) GetSimplifiedRatio(int width, int height)
    {
        var gcd = Gcd(width, height);
        return (width / gcd, height / gcd);
    }

    private static int Gcd(int a, int b)
    {
        while (b != 0)
        {
            var temp = b;
            b = a % b;
            a = temp;
        }
        return a;
    }
}

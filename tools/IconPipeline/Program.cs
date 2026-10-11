using ImageMagick;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: IconPipeline <repo-root>");
    Environment.Exit(1);
}

var repoRoot = Path.GetFullPath(args[0]);
var assets = Path.Combine(repoRoot, "CropCanvas", "Assets");
var master = Path.Combine(assets, "AppIcon_1024.png");
var store = Path.Combine(assets, "Store");

if (!File.Exists(master))
{
    Console.Error.WriteLine($"Missing master image: {master}");
    Environment.Exit(1);
}

Directory.CreateDirectory(store);

using var source = new MagickImage(master);
source.Format = MagickFormat.Png;

void WriteSquare(MagickImage src, int size, string path)
{
    using var img = (MagickImage)src.Clone();
    img.Resize(new MagickGeometry((uint)size, (uint)size) { IgnoreAspectRatio = false });
    img.Write(path);
}

// MSIX / Package.appxmanifest — base scale-100 assets (Partner Center / Store packaging)
WriteSquare(source, 44, Path.Combine(store, "Square44x44Logo.png"));
WriteSquare(source, 71, Path.Combine(store, "Square71x71Logo.png"));
WriteSquare(source, 150, Path.Combine(store, "Square150x150Logo.png"));
WriteSquare(source, 310, Path.Combine(store, "Square310x310Logo.png"));
WriteSquare(source, 50, Path.Combine(store, "StoreLogo.png"));

// Wide tile: centered mark on brand background
var bg = new MagickColor(30, 30, 46); // #1E1E2E
using (var wide = new MagickImage(bg, 310, 150))
using (var logo = (MagickImage)source.Clone())
{
    logo.Resize(new MagickGeometry(120, 120) { Greater = true, Less = true, IgnoreAspectRatio = false });
    wide.Composite(logo, Gravity.Center, CompositeOperator.Over);
    wide.Write(Path.Combine(store, "Wide310x150Logo.png"));
}

// App list target sizes (Explorer / Start) — names match Package VisualElements / MSIX conventions
foreach (var ts in new[] { 16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256 })
{
    WriteSquare(source, ts, Path.Combine(store, $"Square44x44Logo.targetsize-{ts}.png"));
}

// Partner Center store listing (large icon — PNG, use as submitted “app icon” art)
WriteSquare(source, 512, Path.Combine(store, "PartnerCenter_Listing_512.png"));
WriteSquare(source, 1024, Path.Combine(store, "PartnerCenter_Listing_1024.png"));

// Multi-resolution .ico for WPF title bar + .exe (16–256)
using (var collection = new MagickImageCollection())
{
    foreach (var s in new[] { 16, 24, 32, 48, 64, 128, 256 })
    {
        var frame = (MagickImage)source.Clone();
        frame.Resize(new MagickGeometry((uint)s, (uint)s) { IgnoreAspectRatio = false });
        collection.Add(frame);
    }

    // Stream + explicit format: writing to "*.ico" path can emit one file per frame on some IM builds.
    using var ms = new MemoryStream();
    collection.Write(ms, MagickFormat.Ico);
    File.WriteAllBytes(Path.Combine(assets, "CropCanvas.ico"), ms.ToArray());
}

Console.WriteLine($"Wrote ICO and Store assets under {assets}");

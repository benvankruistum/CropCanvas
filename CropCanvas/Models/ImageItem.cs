using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CropCanvas.Models;

public partial class ImageItem : ObservableObject
{
    public string FilePath { get; }
    public string FileName { get; }
    public int OriginalWidth { get; set; }
    public int OriginalHeight { get; set; }

    [ObservableProperty]
    private BitmapSource? _thumbnail;

    [ObservableProperty]
    private bool _isCropped;

    public ImageItem(string filePath)
    {
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);
    }
}

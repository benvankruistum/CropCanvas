using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CropCanvas.ViewModels;

namespace CropCanvas.Views;

public partial class MainWindow : Window
{
    private double _zoomLevel = 1.0;
    private const double ZoomMin = 0.2;
    private const double ZoomMax = 5.0;
    private const double ZoomStep = 0.15;

    public MainWindow()
    {
        InitializeComponent();

        if (DataContext is MainViewModel vm)
            SubscribeToViewModel(vm);

        DataContextChanged += (s, e) =>
        {
            if (e.NewValue is MainViewModel newVm)
                SubscribeToViewModel(newVm);
        };
    }

    private void SubscribeToViewModel(MainViewModel vm)
    {
        vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is not MainViewModel vm) return;

        switch (args.PropertyName)
        {
            case nameof(MainViewModel.DisplayImage):
                if (vm.DisplayImage is BitmapSource bmp)
                    CropOverlayControl.ImageAspectRatio = bmp.Width / bmp.Height;
                else
                    CropOverlayControl.ImageAspectRatio = 0;
                // Reset zoom when new image loads
                SetZoom(1.0);
                break;

            case nameof(MainViewModel.NormalizedCropX):
            case nameof(MainViewModel.NormalizedCropY):
            case nameof(MainViewModel.NormalizedCropWidth):
            case nameof(MainViewModel.NormalizedCropHeight):
                vm.OnCropRegionChanged();
                break;
        }
    }

    private void SetZoom(double level)
    {
        _zoomLevel = Math.Clamp(level, ZoomMin, ZoomMax);
        CanvasZoom.ScaleX = _zoomLevel;
        CanvasZoom.ScaleY = _zoomLevel;
        ZoomLabel.Text = $"{(int)(_zoomLevel * 100)}%";
    }

    private void OnZoomIn(object sender, RoutedEventArgs e)
    {
        SetZoom(_zoomLevel + ZoomStep);
    }

    private void OnZoomOut(object sender, RoutedEventArgs e)
    {
        SetZoom(_zoomLevel - ZoomStep);
    }

    private void OnZoomReset(object sender, RoutedEventArgs e)
    {
        SetZoom(1.0);
    }

    private void OnZoom100(object sender, RoutedEventArgs e)
    {
        // Calculate zoom level where image pixels = screen pixels
        if (DataContext is MainViewModel vm && vm.SelectedImage != null && MainImage.ActualWidth > 0)
        {
            var pixelZoom = (double)vm.SelectedImage.OriginalWidth / MainImage.ActualWidth;
            SetZoom(pixelZoom);
        }
    }

    private void OnCanvasMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            var delta = e.Delta > 0 ? ZoomStep : -ZoomStep;
            SetZoom(_zoomLevel + delta);
            e.Handled = true;
        }
    }
}

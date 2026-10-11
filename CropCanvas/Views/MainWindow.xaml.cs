using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CropCanvas.ViewModels;

namespace CropCanvas.Views;

public partial class MainWindow : Window
{
    private double _zoomLevel = 1.0;
    private const double ZoomMin = 0.2;
    private const double ZoomMax = 5.0;
    private const double ZoomStep = 0.15;

    /// <summary>Last selected file path we centered the scroll for; rotation-only updates reuse the path and skip re-center.</summary>
    private string? _lastScrollCenteredForImagePath;

    public MainWindow()
    {
        InitializeComponent();
        UpdateMaximizeGlyph();

        if (DataContext is MainViewModel vm)
            SubscribeToViewModel(vm);

        DataContextChanged += (s, e) =>
        {
            if (e.NewValue is MainViewModel newVm)
                SubscribeToViewModel(newVm);
        };

        // Auto-scroll log to bottom
        Services.LogService.Instance.Entries.CollectionChanged += (s, e) =>
        {
            if (LogList.Items.Count > 0)
                LogList.ScrollIntoView(LogList.Items[^1]);
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
            case nameof(MainViewModel.SelectedImage):
                if (vm.SelectedImage == null)
                {
                    MainImage.ClearValue(FrameworkElement.MaxWidthProperty);
                    MainImage.ClearValue(FrameworkElement.MaxHeightProperty);
                    SetZoom(1.0);
                    _lastScrollCenteredForImagePath = null;
                }

                break;

            case nameof(MainViewModel.DisplayImage):
                if (vm.DisplayImage is BitmapSource bmp)
                {
                    CropOverlayControl.ImageAspectRatio = bmp.PixelWidth > 0 && bmp.PixelHeight > 0
                        ? bmp.PixelWidth / (double)bmp.PixelHeight
                        : bmp.Width / bmp.Height;

                    // Rotated bitmaps change aspect ratio; Uniform would otherwise grow the layout (feels like zoom-in).
                    // Lock measure box to the 0° decode size; while tilting, caps stay so layout cannot expand.
                    if (Math.Abs(vm.RotationDegrees) < 1e-5)
                    {
                        MainImage.MaxWidth = bmp.PixelWidth;
                        MainImage.MaxHeight = bmp.PixelHeight;
                    }
                }
                else
                {
                    CropOverlayControl.ImageAspectRatio = 0;
                    MainImage.ClearValue(FrameworkElement.MaxWidthProperty);
                    MainImage.ClearValue(FrameworkElement.MaxHeightProperty);
                }

                // Re-apply zoom so LayoutTransform + crop overlay track the new bitmap without resetting level.
                if (vm.SelectedImage != null && vm.DisplayImage != null)
                {
                    SetZoom(_zoomLevel);
                    var path = vm.SelectedImage.FilePath;
                    if (!string.Equals(path, _lastScrollCenteredForImagePath, StringComparison.OrdinalIgnoreCase))
                    {
                        _lastScrollCenteredForImagePath = path;
                        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, CenterCanvasScroll);
                    }
                }

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
        ZoomLabel.Text = $"{(int)(_zoomLevel * 100)}%";
        // Nieuwe ScaleTransform-instance: LayoutTransform-property wijzigt → Image meet opnieuw.
        // Alleen ScaleX/Y muteren op hetzelfde object triggert dat in WPF niet betrouwbaar, waardoor
        // ActualWidth vastliep en crop (Normalized × inner) niet meeschaalde.
        MainImage.LayoutTransform = new ScaleTransform(_zoomLevel, _zoomLevel);
        MainImage.InvalidateMeasure();
        MainImage.InvalidateArrange();
        ImageContainer.UpdateLayout();
        CropOverlayControl.SyncLayout();
    }

    /// <summary>Scroll the image canvas so content is centered (after layout / extent is known).</summary>
    private void CenterCanvasScroll()
    {
        CanvasScroller.UpdateLayout();
        var extentW = CanvasScroller.ExtentWidth;
        var extentH = CanvasScroller.ExtentHeight;
        var viewW = CanvasScroller.ViewportWidth;
        var viewH = CanvasScroller.ViewportHeight;
        if (extentW <= 0 || extentH <= 0 || viewW <= 0 || viewH <= 0)
            return;

        var x = Math.Max(0, (extentW - viewW) / 2);
        var y = Math.Max(0, (extentH - viewH) / 2);
        CanvasScroller.ScrollToHorizontalOffset(x);
        CanvasScroller.ScrollToVerticalOffset(y);
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
        // Calculate zoom level where image pixels = screen pixels (after straightening)
        if (DataContext is MainViewModel vm && vm.SelectedImage != null && MainImage.ActualWidth > 0)
        {
            var pixelZoom = (double)vm.EffectiveImageWidth / MainImage.ActualWidth;
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

    private void OnToggleLog(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.IsLogVisible = !vm.IsLogVisible;
    }

    private void OnClearLog(object sender, RoutedEventArgs e)
    {
        CropCanvas.Services.LogService.Instance.Clear();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2 && ResizeMode == ResizeMode.CanResize)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }
        try
        {
            DragMove();
        }
        catch
        {
            // DragMove kan falen vóór volledige window-initialisatie.
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_StateChanged(object? sender, EventArgs e) => UpdateMaximizeGlyph();

    private void UpdateMaximizeGlyph()
    {
        if (MaximizeIconGlyph == null) return;
        // Tabler: window-maximize U+F1F1, window-restore (icon restore) U+FAFD
        MaximizeIconGlyph.Text = WindowState == WindowState.Maximized ? "\uFAFD" : "\uF1F1";
    }
}

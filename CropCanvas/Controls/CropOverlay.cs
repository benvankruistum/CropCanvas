using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace CropCanvas.Controls;

public class CropOverlay : Canvas
{
    private const double HandleSize = 10;

    private static readonly Brush DimBrush = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0));
    private static readonly Brush HandleBrush = new SolidColorBrush(Color.FromRgb(0x89, 0xB4, 0xFA));
    private static readonly Brush CropBorderBrush = new SolidColorBrush(Color.FromRgb(0x89, 0xB4, 0xFA));
    private static readonly Brush WarningBorderBrush = new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8));

    // Dim rectangles around crop window
    private readonly Rectangle _dimTop = new() { Fill = DimBrush, IsHitTestVisible = false };
    private readonly Rectangle _dimBottom = new() { Fill = DimBrush, IsHitTestVisible = false };
    private readonly Rectangle _dimLeft = new() { Fill = DimBrush, IsHitTestVisible = false };
    private readonly Rectangle _dimRight = new() { Fill = DimBrush, IsHitTestVisible = false };

    // Warning overlays for out-of-bounds areas
    private static readonly Brush WarningBrushFill = new SolidColorBrush(Color.FromArgb(140, 0xF3, 0x8B, 0xA8));
    private readonly Rectangle _warnTop = new() { Fill = WarningBrushFill, IsHitTestVisible = false };
    private readonly Rectangle _warnBottom = new() { Fill = WarningBrushFill, IsHitTestVisible = false };
    private readonly Rectangle _warnLeft = new() { Fill = WarningBrushFill, IsHitTestVisible = false };
    private readonly Rectangle _warnRight = new() { Fill = WarningBrushFill, IsHitTestVisible = false };

    // "AI" label in the OOB area
    private readonly Border _aiLabel = new()
    {
        Background = new SolidColorBrush(Color.FromArgb(180, 0x89, 0xB4, 0xFA)),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(12, 6, 12, 6),
        IsHitTestVisible = false,
        Child = new TextBlock
        {
            Text = "\u2728 AI",
            Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x1B)),
            FontSize = 13,
            FontWeight = System.Windows.FontWeights.Bold,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
        }
    };

    // Rule of thirds grid
    private readonly Line[] _gridShadows = CreateGridLines(new SolidColorBrush(Color.FromArgb(100, 0, 0, 0)), null, 1.5);
    private readonly Line[] _gridLines = CreateGridLines(new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
        new DoubleCollection { 4, 4 }, 1.0);

    private static Line[] CreateGridLines(Brush stroke, DoubleCollection? dashArray, double thickness)
    {
        var lines = new Line[4];
        for (int i = 0; i < 4; i++)
        {
            lines[i] = new Line
            {
                Stroke = stroke,
                StrokeThickness = thickness,
                StrokeDashArray = dashArray,
                IsHitTestVisible = false,
                SnapsToDevicePixels = true
            };
        }
        return lines;
    }

    // Crop border
    private readonly Rectangle _cropBorder = new()
    {
        Stroke = CropBorderBrush,
        StrokeThickness = 2,
        Fill = Brushes.Transparent,
        Cursor = Cursors.SizeAll
    };

    /// <summary>Optional horizontal horizon guide for straightening (draggable vertically).</summary>
    private readonly Line _horizonGuideLine = new()
    {
        Stroke = new SolidColorBrush(Color.FromRgb(0xF9, 0xE2, 0xAF)),
        StrokeThickness = 2,
        SnapsToDevicePixels = true,
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed
    };

    private bool _isDraggingHorizonGuide;

    // Corner handles
    private readonly Rectangle _handleTL = CreateHandle(Cursors.SizeNWSE);
    private readonly Rectangle _handleTR = CreateHandle(Cursors.SizeNESW);
    private readonly Rectangle _handleBL = CreateHandle(Cursors.SizeNESW);
    private readonly Rectangle _handleBR = CreateHandle(Cursors.SizeNWSE);

    // Interaction handler
    private readonly CropInteractionHandler _interaction = new();

    // Crop rect in display coords (relative to rendered image area)
    private double _cropX, _cropY, _cropW, _cropH;

    // Rendered image bounds within this control
    private double _imgOffsetX, _imgOffsetY, _imgRenderW, _imgRenderH;

    #region Dependency Properties

    public static readonly DependencyProperty AspectRatioProperty =
        DependencyProperty.Register(nameof(AspectRatio), typeof(double), typeof(CropOverlay),
            new PropertyMetadata(16.0 / 9.0, OnAspectRatioChanged));

    public double AspectRatio
    {
        get => (double)GetValue(AspectRatioProperty);
        set => SetValue(AspectRatioProperty, value);
    }

    public static readonly DependencyProperty NormalizedXProperty =
        DependencyProperty.Register(nameof(NormalizedX), typeof(double), typeof(CropOverlay),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnNormalizedChanged));

    public double NormalizedX
    {
        get => (double)GetValue(NormalizedXProperty);
        set => SetValue(NormalizedXProperty, value);
    }

    public static readonly DependencyProperty NormalizedYProperty =
        DependencyProperty.Register(nameof(NormalizedY), typeof(double), typeof(CropOverlay),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnNormalizedChanged));

    public double NormalizedY
    {
        get => (double)GetValue(NormalizedYProperty);
        set => SetValue(NormalizedYProperty, value);
    }

    public static readonly DependencyProperty NormalizedWidthProperty =
        DependencyProperty.Register(nameof(NormalizedWidth), typeof(double), typeof(CropOverlay),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnNormalizedChanged));

    public double NormalizedWidth
    {
        get => (double)GetValue(NormalizedWidthProperty);
        set => SetValue(NormalizedWidthProperty, value);
    }

    public static readonly DependencyProperty NormalizedHeightProperty =
        DependencyProperty.Register(nameof(NormalizedHeight), typeof(double), typeof(CropOverlay),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnNormalizedChanged));

    public double NormalizedHeight
    {
        get => (double)GetValue(NormalizedHeightProperty);
        set => SetValue(NormalizedHeightProperty, value);
    }

    public static readonly DependencyProperty ImageAspectRatioProperty =
        DependencyProperty.Register(nameof(ImageAspectRatio), typeof(double), typeof(CropOverlay),
            new PropertyMetadata(0.0, OnImageAspectRatioChanged));

    public double ImageAspectRatio
    {
        get => (double)GetValue(ImageAspectRatioProperty);
        set => SetValue(ImageAspectRatioProperty, value);
    }

    private static void OnImageAspectRatioChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CropOverlay overlay)
            overlay.RecalculateFromNormalized();
    }

    public static readonly DependencyProperty HasOutOfBoundsProperty =
        DependencyProperty.Register(nameof(HasOutOfBounds), typeof(bool), typeof(CropOverlay),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public bool HasOutOfBounds
    {
        get => (bool)GetValue(HasOutOfBoundsProperty);
        set => SetValue(HasOutOfBoundsProperty, value);
    }

    public static readonly DependencyProperty AllowOutOfBoundsProperty =
        DependencyProperty.Register(nameof(AllowOutOfBounds), typeof(bool), typeof(CropOverlay),
            new PropertyMetadata(false));

    public bool AllowOutOfBounds
    {
        get => (bool)GetValue(AllowOutOfBoundsProperty);
        set => SetValue(AllowOutOfBoundsProperty, value);
    }

    public static readonly DependencyProperty ShowHorizonGuideProperty =
        DependencyProperty.Register(nameof(ShowHorizonGuide), typeof(bool), typeof(CropOverlay),
            new PropertyMetadata(false, OnShowHorizonGuideChanged));

    public bool ShowHorizonGuide
    {
        get => (bool)GetValue(ShowHorizonGuideProperty);
        set => SetValue(ShowHorizonGuideProperty, value);
    }

    /// <summary>0 = top of rendered image, 1 = bottom — horizontal guide Y position.</summary>
    public static readonly DependencyProperty HorizonGuideNormalizedYProperty =
        DependencyProperty.Register(nameof(HorizonGuideNormalizedY), typeof(double), typeof(CropOverlay),
            new FrameworkPropertyMetadata(0.5, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnHorizonGuideYChanged));

    public double HorizonGuideNormalizedY
    {
        get => (double)GetValue(HorizonGuideNormalizedYProperty);
        set => SetValue(HorizonGuideNormalizedYProperty, value);
    }

    private static void OnShowHorizonGuideChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not CropOverlay o) return;
        if (e.NewValue is false && o._isDraggingHorizonGuide)
        {
            o._isDraggingHorizonGuide = false;
            o.ReleaseMouseCapture();
        }

        o.UpdateHorizonGuideLayout();
    }

    private static void OnHorizonGuideYChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CropOverlay o)
            o.UpdateHorizonGuideLayout();
    }

    #endregion

    public CropOverlay()
    {
        ClipToBounds = true;
        Background = Brushes.Transparent;

        Children.Add(_dimTop);
        Children.Add(_dimBottom);
        Children.Add(_dimLeft);
        Children.Add(_dimRight);
        Children.Add(_cropBorder);
        Children.Add(_horizonGuideLine);
        foreach (var line in _gridShadows) Children.Add(line);
        foreach (var line in _gridLines) Children.Add(line);
        Children.Add(_warnTop);
        Children.Add(_warnBottom);
        Children.Add(_warnLeft);
        Children.Add(_warnRight);
        Children.Add(_aiLabel);
        Children.Add(_handleTL);
        Children.Add(_handleTR);
        Children.Add(_handleBL);
        Children.Add(_handleBR);

        _cropBorder.MouseLeftButtonDown += CropBorder_MouseDown;
        _handleTL.MouseLeftButtonDown += (s, e) => StartResize(0, e);
        _handleTR.MouseLeftButtonDown += (s, e) => StartResize(1, e);
        _handleBL.MouseLeftButtonDown += (s, e) => StartResize(2, e);
        _handleBR.MouseLeftButtonDown += (s, e) => StartResize(3, e);

        SizeChanged += (_, _) => RecalculateFromNormalized();
    }

    private const double HorizonGuideHitTolerance = 10;

    private void UpdateHorizonGuideLayout()
    {
        if (!ShowHorizonGuide || _imgRenderW <= 0 || _imgRenderH <= 0)
        {
            _horizonGuideLine.Visibility = Visibility.Collapsed;
            return;
        }

        _horizonGuideLine.Visibility = Visibility.Visible;
        var y = _imgOffsetY + HorizonGuideNormalizedY * _imgRenderH;
        _horizonGuideLine.X1 = _imgOffsetX;
        _horizonGuideLine.X2 = _imgOffsetX + _imgRenderW;
        _horizonGuideLine.Y1 = y;
        _horizonGuideLine.Y2 = y;
    }

    private bool IsOverHorizonGuide(Point p)
    {
        if (!ShowHorizonGuide || _imgRenderW <= 0 || _imgRenderH <= 0) return false;
        var yLine = _imgOffsetY + HorizonGuideNormalizedY * _imgRenderH;
        return p.X >= _imgOffsetX - 4 && p.X <= _imgOffsetX + _imgRenderW + 4
               && Math.Abs(p.Y - yLine) <= HorizonGuideHitTolerance;
    }

    private void ApplyHorizonGuideFromPoint(Point p)
    {
        if (_imgRenderH <= 0) return;
        var n = (p.Y - _imgOffsetY) / _imgRenderH;
        HorizonGuideNormalizedY = Math.Clamp(n, 0, 1);
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (ShowHorizonGuide && IsOverHorizonGuide(e.GetPosition(this)))
        {
            _isDraggingHorizonGuide = true;
            CaptureMouse();
            ApplyHorizonGuideFromPoint(e.GetPosition(this));
            e.Handled = true;
            return;
        }

        base.OnPreviewMouseLeftButtonDown(e);
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        if (_isDraggingHorizonGuide)
        {
            ApplyHorizonGuideFromPoint(e.GetPosition(this));
            e.Handled = true;
            return;
        }

        base.OnPreviewMouseMove(e);
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_isDraggingHorizonGuide)
        {
            _isDraggingHorizonGuide = false;
            ReleaseMouseCapture();
            e.Handled = true;
            return;
        }

        base.OnPreviewMouseLeftButtonUp(e);
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        _isDraggingHorizonGuide = false;
        base.OnLostMouseCapture(e);
    }

    /// <summary>O.a. na zoom aanroepen als SizeChanged niet vuurt (zelfde gridgrootte).</summary>
    public void SyncLayout() => RecalculateFromNormalized();

    private static Rectangle CreateHandle(Cursor cursor) => new()
    {
        Width = HandleSize,
        Height = HandleSize,
        Fill = HandleBrush,
        Stroke = Brushes.White,
        StrokeThickness = 1,
        Cursor = cursor
    };

    /// <summary>
    /// Bitmap-rechthoek t.o.v. deze overlay: het Image-control is vaak groter dan de getekende foto
    /// (Stretch Uniform = letterbox binnen het control). Alleen de inner bounds mappen op genormaliseerde UV's.
    /// Aspect komt uit de echte bron (PixelWidth/Height); anders is de inner box te smal (vooral ultrawide).
    /// </summary>
    private bool TrySyncImageRectFromSibling()
    {
        if (Parent is not Grid grid) return false;
        foreach (UIElement child in grid.Children)
        {
            if (child is System.Windows.Controls.Image img && img.ActualWidth > 0 && img.ActualHeight > 0)
            {
                double aspect = ImageAspectRatio;
                if (img.Source is BitmapSource bs && bs.PixelWidth > 0 && bs.PixelHeight > 0)
                    aspect = (double)bs.PixelWidth / bs.PixelHeight;
                if (aspect <= 0)
                    aspect = ImageAspectRatio;

                // Inner in lokale Image-coördinaten; TranslatePoint zet hoeken goed om, maar inner.RenderW/H
                // zijn lokale afmetingen en worden door LayoutTransform niet op die waarde gezet — alleen
                // volledige transform van hoekpunten geeft de echte breedte/hoogte in overlay-pixels.
                var inner = CropOverlayRenderer.ComputeImageBounds(aspect, img.ActualWidth, img.ActualHeight);
                var toOverlay = img.TransformToVisual(this);
                var p00 = toOverlay.Transform(new Point(inner.OffsetX, inner.OffsetY));
                var p10 = toOverlay.Transform(new Point(inner.OffsetX + inner.RenderW, inner.OffsetY));
                var p01 = toOverlay.Transform(new Point(inner.OffsetX, inner.OffsetY + inner.RenderH));
                var p11 = toOverlay.Transform(new Point(inner.OffsetX + inner.RenderW, inner.OffsetY + inner.RenderH));
                var minX = Math.Min(Math.Min(p00.X, p10.X), Math.Min(p01.X, p11.X));
                var maxX = Math.Max(Math.Max(p00.X, p10.X), Math.Max(p01.X, p11.X));
                var minY = Math.Min(Math.Min(p00.Y, p10.Y), Math.Min(p01.Y, p11.Y));
                var maxY = Math.Max(Math.Max(p00.Y, p10.Y), Math.Max(p01.Y, p11.Y));
                _imgOffsetX = minX;
                _imgOffsetY = minY;
                _imgRenderW = maxX - minX;
                _imgRenderH = maxY - minY;
                return true;
            }
        }

        return false;
    }

    private void RecalculateFromNormalized()
    {
        if (!TrySyncImageRectFromSibling())
        {
            var bounds = CropOverlayRenderer.ComputeImageBounds(ImageAspectRatio, ActualWidth, ActualHeight);
            _imgOffsetX = bounds.OffsetX;
            _imgOffsetY = bounds.OffsetY;
            _imgRenderW = bounds.RenderW;
            _imgRenderH = bounds.RenderH;
        }

        if (_imgRenderW <= 0 || _imgRenderH <= 0) return;

        _cropX = NormalizedX * _imgRenderW;
        _cropY = NormalizedY * _imgRenderH;
        _cropW = NormalizedWidth * _imgRenderW;
        _cropH = NormalizedHeight * _imgRenderH;
        UpdateVisuals();
    }

    private void UpdateNormalizedFromCrop()
    {
        if (_imgRenderW <= 0 || _imgRenderH <= 0) return;
        NormalizedX = _cropX / _imgRenderW;
        NormalizedY = _cropY / _imgRenderH;
        NormalizedWidth = _cropW / _imgRenderW;
        NormalizedHeight = _cropH / _imgRenderH;
    }

    private static void OnNormalizedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CropOverlay overlay && !overlay._interaction.IsDragging && !overlay._interaction.IsResizing)
            overlay.RecalculateFromNormalized();
    }

    private static void OnAspectRatioChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CropOverlay overlay)
            overlay.RecalculateFromNormalized();
    }

    private void UpdateVisuals()
    {
        HasOutOfBounds = CropOverlayRenderer.UpdateVisuals(
            _imgOffsetX, _imgOffsetY, _imgRenderW, _imgRenderH,
            _cropX, _cropY, _cropW, _cropH,
            ActualWidth, ActualHeight,
            _dimTop, _dimBottom, _dimLeft, _dimRight,
            _warnTop, _warnBottom, _warnLeft, _warnRight,
            _aiLabel,
            _cropBorder, CropBorderBrush, WarningBorderBrush,
            _gridShadows, _gridLines,
            _handleTL, _handleTR, _handleBL, _handleBR,
            HandleSize);

        UpdateHorizonGuideLayout();
    }

    #region Mouse Interaction -- Drag

    private void CropBorder_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _interaction.StartDrag(e, e.GetPosition(this), _cropX, _cropY, _cropBorder);

        _cropBorder.MouseMove += CropBorder_MouseMove;
        _cropBorder.MouseLeftButtonUp += CropBorder_MouseUp;
    }

    private void CropBorder_MouseMove(object sender, MouseEventArgs e)
    {
        var (newX, newY) = _interaction.ProcessDragMove(
            e.GetPosition(this), _cropW, _cropH, _imgRenderW, _imgRenderH, AllowOutOfBounds);

        _cropX = newX;
        _cropY = newY;

        UpdateNormalizedFromCrop();
        UpdateVisuals();
    }

    private void CropBorder_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _interaction.EndDrag(_cropBorder);
        _cropBorder.MouseMove -= CropBorder_MouseMove;
        _cropBorder.MouseLeftButtonUp -= CropBorder_MouseUp;
    }

    #endregion

    #region Mouse Interaction -- Resize

    private void StartResize(int corner, MouseButtonEventArgs e)
    {
        var handle = corner switch
        {
            0 => _handleTL,
            1 => _handleTR,
            2 => _handleBL,
            _ => _handleBR
        };

        _interaction.StartResize(corner, e, e.GetPosition(this), _cropX, _cropY, _cropW, _cropH, handle);

        MouseMove += Resize_MouseMove;
        MouseLeftButtonUp += Resize_MouseUp;
    }

    private void Resize_MouseMove(object sender, MouseEventArgs e)
    {
        var result = _interaction.ProcessResizeMove(
            e.GetPosition(this), AspectRatio, _imgRenderW, _imgRenderH, AllowOutOfBounds);

        if (result == null) return;

        var (newX, newY, newW, newH) = result.Value;
        _cropX = newX;
        _cropY = newY;
        _cropW = newW;
        _cropH = newH;

        UpdateNormalizedFromCrop();
        UpdateVisuals();
    }

    private void Resize_MouseUp(object sender, MouseButtonEventArgs e)
    {
        var handle = _interaction.GetResizeHandle(_handleTL, _handleTR, _handleBL, _handleBR);
        _interaction.EndResize(handle);

        MouseMove -= Resize_MouseMove;
        MouseLeftButtonUp -= Resize_MouseUp;
    }

    #endregion
}

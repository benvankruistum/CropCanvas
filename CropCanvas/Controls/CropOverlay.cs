using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

        SizeChanged += (s, e) => RecalculateFromNormalized();
    }

    private static Rectangle CreateHandle(Cursor cursor) => new()
    {
        Width = HandleSize,
        Height = HandleSize,
        Fill = HandleBrush,
        Stroke = Brushes.White,
        StrokeThickness = 1,
        Cursor = cursor
    };

    private void RecalculateFromNormalized()
    {
        var bounds = CropOverlayRenderer.ComputeImageBounds(ImageAspectRatio, ActualWidth, ActualHeight);
        _imgOffsetX = bounds.OffsetX;
        _imgOffsetY = bounds.OffsetY;
        _imgRenderW = bounds.RenderW;
        _imgRenderH = bounds.RenderH;

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

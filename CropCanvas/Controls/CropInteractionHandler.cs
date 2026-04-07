using System.Windows.Input;

namespace CropCanvas.Controls;

public class CropInteractionHandler
{
    private const double SnapDistance = 20;
    private const double MinCropSize = 40;

    // Drag state
    private bool _isDragging;
    private bool _isResizing;
    private int _resizeCorner; // 0=TL, 1=TR, 2=BL, 3=BR
    private Point _dragStart;
    private double _cropStartX, _cropStartY, _cropStartW, _cropStartH;

    public bool IsDragging => _isDragging;
    public bool IsResizing => _isResizing;

    public delegate void CropChangedHandler(double cropX, double cropY, double cropW, double cropH);

    public void StartDrag(MouseButtonEventArgs e, Point position, double cropX, double cropY,
        Rectangle cropBorder)
    {
        _isDragging = true;
        _dragStart = position;
        _cropStartX = cropX;
        _cropStartY = cropY;
        cropBorder.CaptureMouse();
        e.Handled = true;
    }

    public (double newX, double newY) ProcessDragMove(Point position, double cropW, double cropH,
        double imgRenderW, double imgRenderH, bool allowOutOfBounds)
    {
        if (!_isDragging) return (_cropStartX, _cropStartY);

        var dx = position.X - _dragStart.X;
        var dy = position.Y - _dragStart.Y;

        var newX = _cropStartX + dx;
        var newY = _cropStartY + dy;

        // Shift = snap to image edges
        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
        {
            newX = SnapToEdge(newX, 0);
            newY = SnapToEdge(newY, 0);
            newX = SnapToEdge(newX, imgRenderW - cropW);
            newY = SnapToEdge(newY, imgRenderH - cropH);
        }

        if (allowOutOfBounds)
        {
            newX = Clamp(newX, -cropW + MinCropSize, imgRenderW - MinCropSize);
            newY = Clamp(newY, -cropH + MinCropSize, imgRenderH - MinCropSize);
        }
        else
        {
            newX = Clamp(newX, 0, Math.Max(0, imgRenderW - cropW));
            newY = Clamp(newY, 0, Math.Max(0, imgRenderH - cropH));
        }

        return (newX, newY);
    }

    public void EndDrag(Rectangle cropBorder)
    {
        _isDragging = false;
        cropBorder.ReleaseMouseCapture();
    }

    public void StartResize(int corner, MouseButtonEventArgs e, Point position,
        double cropX, double cropY, double cropW, double cropH,
        Rectangle handle)
    {
        _isResizing = true;
        _resizeCorner = corner;
        _dragStart = position;
        _cropStartX = cropX;
        _cropStartY = cropY;
        _cropStartW = cropW;
        _cropStartH = cropH;
        handle.CaptureMouse();
        e.Handled = true;
    }

    public (double newX, double newY, double newW, double newH)? ProcessResizeMove(
        Point position, double aspectRatio, double imgRenderW, double imgRenderH, bool allowOutOfBounds)
    {
        if (!_isResizing) return null;

        var dx = position.X - _dragStart.X;
        var dy = position.Y - _dragStart.Y;

        double newX = _cropStartX, newY = _cropStartY, newW = _cropStartW, newH = _cropStartH;

        switch (_resizeCorner)
        {
            case 3: // BR - anchor TL
                newW = _cropStartW + dx;
                newH = newW / aspectRatio;
                break;
            case 0: // TL - anchor BR
                newW = _cropStartW - dx;
                newH = newW / aspectRatio;
                newX = _cropStartX + _cropStartW - newW;
                newY = _cropStartY + _cropStartH - newH;
                break;
            case 1: // TR - anchor BL
                newW = _cropStartW + dx;
                newH = newW / aspectRatio;
                newY = _cropStartY + _cropStartH - newH;
                break;
            case 2: // BL - anchor TR
                newW = _cropStartW - dx;
                newH = newW / aspectRatio;
                newX = _cropStartX + _cropStartW - newW;
                break;
        }

        if (newW < MinCropSize || newH < MinCropSize) return null;

        // Shift = snap edges to image bounds
        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
        {
            ApplySnapForResize(ref newX, ref newY, ref newW, ref newH, aspectRatio, imgRenderW, imgRenderH);
            if (newW < MinCropSize || newH < MinCropSize) return null;
        }

        // Clamp to image bounds if AI not enabled
        if (!allowOutOfBounds)
            ClampResizeToImage(ref newX, ref newY, ref newW, ref newH, aspectRatio, imgRenderW, imgRenderH);

        return (newX, newY, newW, newH);
    }

    public Rectangle GetResizeHandle(Rectangle handleTL, Rectangle handleTR, Rectangle handleBL, Rectangle handleBR)
    {
        return _resizeCorner switch
        {
            0 => handleTL,
            1 => handleTR,
            2 => handleBL,
            _ => handleBR
        };
    }

    public void EndResize(Rectangle handle)
    {
        _isResizing = false;
        handle.ReleaseMouseCapture();
    }

    private void ApplySnapForResize(ref double newX, ref double newY, ref double newW, ref double newH,
        double aspectRatio, double imgRenderW, double imgRenderH)
    {
        double anchorR = _cropStartX + _cropStartW;
        double anchorB = _cropStartY + _cropStartH;

        switch (_resizeCorner)
        {
            case 0: // TL
                if (Math.Abs(newX) < SnapDistance) { newW = anchorR; newH = newW / aspectRatio; newX = 0; newY = anchorB - newH; }
                if (Math.Abs(newY) < SnapDistance) { newH = anchorB; newW = newH * aspectRatio; newX = anchorR - newW; newY = 0; }
                break;
            case 1: // TR
                if (Math.Abs(newX + newW - imgRenderW) < SnapDistance) { newW = imgRenderW - newX; newH = newW / aspectRatio; newY = anchorB - newH; }
                if (Math.Abs(newY) < SnapDistance) { newH = anchorB; newW = newH * aspectRatio; newY = 0; }
                break;
            case 2: // BL
                if (Math.Abs(newX) < SnapDistance) { newW = anchorR; newH = newW / aspectRatio; newX = 0; }
                if (Math.Abs(newY + newH - imgRenderH) < SnapDistance) { newH = imgRenderH - newY; newW = newH * aspectRatio; newX = anchorR - newW; }
                break;
            case 3: // BR
                if (Math.Abs(newX + newW - imgRenderW) < SnapDistance) { newW = imgRenderW - newX; newH = newW / aspectRatio; }
                if (Math.Abs(newY + newH - imgRenderH) < SnapDistance) { newH = imgRenderH - newY; newW = newH * aspectRatio; }
                break;
        }
    }

    private void ClampResizeToImage(ref double newX, ref double newY, ref double newW, ref double newH,
        double aspectRatio, double imgRenderW, double imgRenderH)
    {
        // Determine the anchor point (the corner that stays fixed)
        double anchorX = (_resizeCorner is 0 or 2) ? _cropStartX + _cropStartW : _cropStartX;
        double anchorY = (_resizeCorner is 0 or 1) ? _cropStartY + _cropStartH : _cropStartY;

        // Max width/height allowed from the anchor point to the image edges
        double maxW, maxH;

        if (_resizeCorner is 1 or 3)
            maxW = imgRenderW - anchorX;
        else
            maxW = anchorX;

        if (_resizeCorner is 2 or 3)
            maxH = imgRenderH - anchorY;
        else
            maxH = anchorY;

        // Apply the most restrictive constraint while preserving aspect ratio
        double constrainedW = Math.Min(newW, maxW);
        double constrainedH = constrainedW / aspectRatio;

        if (constrainedH > maxH)
        {
            constrainedH = maxH;
            constrainedW = constrainedH * aspectRatio;
        }

        newW = constrainedW;
        newH = constrainedH;

        // Recompute position from anchor
        if (_resizeCorner is 0 or 2)
            newX = anchorX - newW;
        else
            newX = anchorX;

        if (_resizeCorner is 0 or 1)
            newY = anchorY - newH;
        else
            newY = anchorY;
    }

    private static double Clamp(double value, double min, double max)
        => value < min ? min : value > max ? max : value;

    private static double SnapToEdge(double value, double edgeValue)
        => Math.Abs(value - edgeValue) < SnapDistance ? edgeValue : value;
}

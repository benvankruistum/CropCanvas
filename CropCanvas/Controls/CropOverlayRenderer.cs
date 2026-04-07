using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace CropCanvas.Controls;

public static class CropOverlayRenderer
{
    public record ImageBounds(double OffsetX, double OffsetY, double RenderW, double RenderH);

    public static ImageBounds ComputeImageBounds(double imageAspectRatio, double canvasW, double canvasH)
    {
        if (imageAspectRatio <= 0 || canvasW <= 0 || canvasH <= 0)
            return new ImageBounds(0, 0, canvasW, canvasH);

        double containerAspect = canvasW / canvasH;

        if (imageAspectRatio > containerAspect)
        {
            var renderW = canvasW;
            var renderH = canvasW / imageAspectRatio;
            return new ImageBounds(0, (canvasH - renderH) / 2, renderW, renderH);
        }
        else
        {
            var renderH = canvasH;
            var renderW = canvasH * imageAspectRatio;
            return new ImageBounds((canvasW - renderW) / 2, 0, renderW, renderH);
        }
    }

    public static bool UpdateVisuals(
        double imgOffsetX, double imgOffsetY, double imgRenderW, double imgRenderH,
        double cropX, double cropY, double cropW, double cropH,
        double canvasW, double canvasH,
        Rectangle dimTop, Rectangle dimBottom, Rectangle dimLeft, Rectangle dimRight,
        Rectangle warnTop, Rectangle warnBottom, Rectangle warnLeft, Rectangle warnRight,
        Border aiLabel,
        Rectangle cropBorder, Brush cropBorderNormal, Brush cropBorderWarning,
        Line[] gridShadows, Line[] gridLines,
        Rectangle handleTL, Rectangle handleTR, Rectangle handleBL, Rectangle handleBR,
        double handleSize)
    {
        // Absolute position of crop in canvas coords
        var absX = imgOffsetX + cropX;
        var absY = imgOffsetY + cropY;

        // Image absolute bounds
        var imgLeft = imgOffsetX;
        var imgTop = imgOffsetY;
        var imgRight = imgOffsetX + imgRenderW;
        var imgBottom = imgOffsetY + imgRenderH;

        // Crop absolute bounds
        var cropLeft = absX;
        var cropTop = absY;
        var cropRight = absX + cropW;
        var cropBottom = absY + cropH;

        // Dim regions (around the crop, within the full canvas)
        SetRect(dimTop, 0, 0, canvasW, cropTop);
        SetRect(dimBottom, 0, cropBottom, canvasW, canvasH - cropBottom);
        SetRect(dimLeft, 0, cropTop, cropLeft, cropH);
        SetRect(dimRight, cropRight, cropTop, canvasW - cropRight, cropH);

        // Warning overlays: parts of the crop that fall outside the image
        var oobLeft = Math.Max(0, imgLeft - cropLeft);
        var oobTop = Math.Max(0, imgTop - cropTop);
        var oobRight = Math.Max(0, cropRight - imgRight);
        var oobBottom = Math.Max(0, cropBottom - imgBottom);

        bool hasOob = oobLeft > 0 || oobTop > 0 || oobRight > 0 || oobBottom > 0;

        // Left warning stripe
        SetRect(warnLeft, cropLeft, cropTop + oobTop, oobLeft, cropH - oobTop - oobBottom);
        // Right warning stripe
        SetRect(warnRight, cropRight - oobRight, cropTop + oobTop, oobRight, cropH - oobTop - oobBottom);
        // Top warning stripe (full width of crop)
        SetRect(warnTop, cropLeft, cropTop, cropW, oobTop);
        // Bottom warning stripe (full width of crop)
        SetRect(warnBottom, cropLeft, cropBottom - oobBottom, cropW, oobBottom);

        warnLeft.Visibility = oobLeft > 0 ? Visibility.Visible : Visibility.Collapsed;
        warnRight.Visibility = oobRight > 0 ? Visibility.Visible : Visibility.Collapsed;
        warnTop.Visibility = oobTop > 0 ? Visibility.Visible : Visibility.Collapsed;
        warnBottom.Visibility = oobBottom > 0 ? Visibility.Visible : Visibility.Collapsed;

        // Position "AI" label in the largest warning area
        if (hasOob)
        {
            double maxArea = 0;
            double labelX = 0, labelY = 0, labelW = 0, labelH = 0;

            if (oobTop * cropW > maxArea) { maxArea = oobTop * cropW; labelX = cropLeft; labelY = cropTop; labelW = cropW; labelH = oobTop; }
            if (oobBottom * cropW > maxArea) { maxArea = oobBottom * cropW; labelX = cropLeft; labelY = cropBottom - oobBottom; labelW = cropW; labelH = oobBottom; }
            if (oobLeft * (cropH - oobTop - oobBottom) > maxArea) { maxArea = oobLeft * cropH; labelX = cropLeft; labelY = cropTop + oobTop; labelW = oobLeft; labelH = cropH - oobTop - oobBottom; }
            if (oobRight * (cropH - oobTop - oobBottom) > maxArea) { labelX = cropRight - oobRight; labelY = cropTop + oobTop; labelW = oobRight; labelH = cropH - oobTop - oobBottom; }

            aiLabel.Visibility = Visibility.Visible;
            aiLabel.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            var bw = aiLabel.DesiredSize.Width;
            var bh = aiLabel.DesiredSize.Height;
            Canvas.SetLeft(aiLabel, labelX + (labelW - bw) / 2);
            Canvas.SetTop(aiLabel, labelY + (labelH - bh) / 2);
        }
        else
        {
            aiLabel.Visibility = Visibility.Collapsed;
        }

        // Crop border -- change color if out of bounds
        cropBorder.Stroke = hasOob ? cropBorderWarning : cropBorderNormal;
        SetRect(cropBorder, absX, absY, cropW, cropH);

        // Rule of thirds grid
        UpdateGridLines(gridShadows, absX, absY, cropW, cropH);
        UpdateGridLines(gridLines, absX, absY, cropW, cropH);

        // Handles
        var hs = handleSize / 2;
        SetRect(handleTL, absX - hs, absY - hs, handleSize, handleSize);
        SetRect(handleTR, absX + cropW - hs, absY - hs, handleSize, handleSize);
        SetRect(handleBL, absX - hs, absY + cropH - hs, handleSize, handleSize);
        SetRect(handleBR, absX + cropW - hs, absY + cropH - hs, handleSize, handleSize);

        return hasOob;
    }

    public static void UpdateGridLines(Line[] lines, double x, double y, double w, double h)
    {
        var h1 = y + h / 3;
        var h2 = y + h * 2 / 3;
        var v1 = x + w / 3;
        var v2 = x + w * 2 / 3;

        SetLine(lines[0], x, h1, x + w, h1);
        SetLine(lines[1], x, h2, x + w, h2);
        SetLine(lines[2], v1, y, v1, y + h);
        SetLine(lines[3], v2, y, v2, y + h);
    }

    public static void SetLine(Line line, double x1, double y1, double x2, double y2)
    {
        line.X1 = x1; line.Y1 = y1;
        line.X2 = x2; line.Y2 = y2;
    }

    public static void SetRect(FrameworkElement el, double x, double y, double w, double h)
    {
        if (w < 0) w = 0;
        if (h < 0) h = 0;
        Canvas.SetLeft(el, x);
        Canvas.SetTop(el, y);
        el.Width = w;
        el.Height = h;
    }
}

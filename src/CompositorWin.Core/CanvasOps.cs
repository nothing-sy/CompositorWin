using SkiaSharp;

namespace CompositorWin.Core;

public static class CanvasOps
{
    public static void ResizeCanvas(CompDocument document, int width, int height, string anchor = "center")
    {
        var dx = Offset(anchor, document.Manifest.Width, width, true);
        var dy = Offset(anchor, document.Manifest.Height, height, false);
        foreach (var layer in document.Manifest.Layers)
        {
            layer.Transform.Origin = [layer.Transform.X + dx, layer.Transform.Y + dy];
        }

        foreach (var g in document.Manifest.Guides ?? [])
        {
            g.Position += g.Axis == "vertical" ? dx : dy;
        }

        document.Manifest.Width = width;
        document.Manifest.Height = height;
        ProjectStore.Save(document);
    }

    public static void ResizeImage(CompDocument document, int width, int height)
    {
        var sx = width / (double)Math.Max(1, document.Manifest.Width);
        var sy = height / (double)Math.Max(1, document.Manifest.Height);
        foreach (var layer in document.Manifest.Layers)
        {
            if (!string.IsNullOrEmpty(layer.ImageFile))
            {
                var path = ProjectValidator.SafeImagePath(document.PackagePath, layer.ImageFile);
                using var src = ImageIO.LoadRgba(path);
                using var resized = src.Resize(new SKImageInfo(Math.Max(1, (int)Math.Round(src.Width * sx)), Math.Max(1, (int)Math.Round(src.Height * sy)), SKColorType.Rgba8888, SKAlphaType.Unpremul), SKFilterQuality.High)
                                   ?? src.Copy();
                ProjectStore.SavePng(path, resized);
            }

            layer.Transform.Origin = [layer.Transform.X * sx, layer.Transform.Y * sy];
            layer.Transform.Size = [layer.Transform.Width * sx, layer.Transform.Height * sy];
        }

        document.Manifest.Width = width;
        document.Manifest.Height = height;
        ProjectStore.Save(document);
    }

    public static void Crop(CompDocument document, int x, int y, int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        foreach (var layer in document.Manifest.Layers)
        {
            layer.Transform.Origin = [layer.Transform.X - x, layer.Transform.Y - y];
        }

        document.Manifest.Width = width;
        document.Manifest.Height = height;
        ProjectStore.Save(document);
    }

    private static double Offset(string anchor, int oldSize, int newSize, bool horizontal)
    {
        var key = anchor.ToLowerInvariant();
        var left = key.Contains("left") || key.Contains("west");
        var right = key.Contains("right") || key.Contains("east");
        var top = key.Contains("top") || key.Contains("north");
        var bottom = key.Contains("bottom") || key.Contains("south");
        if (horizontal)
        {
            if (left) return 0;
            if (right) return newSize - oldSize;
            return (newSize - oldSize) / 2.0;
        }

        if (top) return 0;
        if (bottom) return newSize - oldSize;
        return (newSize - oldSize) / 2.0;
    }
}

using SkiaSharp;

namespace CompositorWin.Core;

public static class DocumentCompositor
{
    public static SKBitmap Composite(CompDocument document, double displayScale = 1)
    {
        var dest = new SKBitmap(document.Manifest.Width, document.Manifest.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using (var canvas = new SKCanvas(dest))
        {
            canvas.Clear(SKColors.Transparent);
        }

        CompositeChildren(document, dest, parentId: null, LayerRasterCache.LevelFor(displayScale));
        return dest;
    }

    private static void CompositeChildren(CompDocument document, SKBitmap dest, string? parentId, int mip)
    {
        foreach (var layer in document.Manifest.Layers)
        {
            var matches = parentId is null ? string.IsNullOrEmpty(layer.ParentId) : string.Equals(layer.ParentId, parentId, StringComparison.OrdinalIgnoreCase);
            if (!matches || !layer.IsVisible)
            {
                continue;
            }

            if (layer.IsGroup)
            {
                var group = new SKBitmap(dest.Width, dest.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                using (var c = new SKCanvas(group))
                {
                    c.Clear(SKColors.Transparent);
                }

                CompositeChildren(document, group, layer.Id, mip);
                SKBitmap? gmask = null;
                if (!string.IsNullOrEmpty(layer.MaskFile) && layer.MaskEnabled != false)
                {
                    gmask = LoadCached(document, layer.MaskFile, mip);
                }

                DrawLayer(dest, group, layer, layer.Opacity, gmask, ownsSource: true);
                continue;
            }

            SKBitmap? mask = LoadMask(document, layer, mip, out var ownsMask);
            try
            {
                if (layer.Adjustment is not null)
                {
                    ImageOps.ApplyAdjustment(dest, layer.Adjustment, mask);
                    continue;
                }

                var src = Rasterize(document, layer, mip, out var owns);
                if (src is null)
                {
                    continue;
                }

                SKBitmap draw = src;
                SKBitmap? effected = null;
                if (layer.Effects is not null)
                {
                    effected = LayerEffectsOps.Apply(src, layer.Effects);
                    draw = effected;
                }

                if (layer.CameraRaw is not null)
                {
                    if (effected is null)
                    {
                        effected = src.Copy();
                        draw = effected!;
                    }

                    CameraRawOps.Apply(draw, layer.CameraRaw);
                }

                try
                {
                    DrawLayer(dest, draw, layer, layer.Opacity, mask, ownsSource: false);
                }
                finally
                {
                    effected?.Dispose();
                    if (owns)
                    {
                        src.Dispose();
                    }
                }
            }
            finally
            {
                if (ownsMask)
                {
                    mask?.Dispose();
                }
            }
        }
    }

    private static SKBitmap? Rasterize(CompDocument document, LayerRecord layer, int mip, out bool owns)
    {
        owns = true;
        if (layer.Text is not null)
        {
            return TextOps.Render(layer.Text, Math.Max(8, (int)layer.Transform.Width), Math.Max(8, (int)layer.Transform.Height));
        }

        if (layer.Shape is not null)
        {
            return PixelTools.RenderShape(Math.Max(8, (int)layer.Transform.Width), Math.Max(8, (int)layer.Transform.Height), layer.Shape);
        }

        if (string.IsNullOrEmpty(layer.ImageFile) || string.IsNullOrEmpty(document.PackagePath))
        {
            owns = false;
            return null;
        }

        owns = false;
        return LoadCached(document, layer.ImageFile, mip);
    }

    private static SKBitmap LoadCached(CompDocument document, string relative, int mip)
    {
        var path = ProjectValidator.SafeImagePath(document.PackagePath, relative);
        return LayerRasterCache.GetShared(path, mip);
    }

    private static SKBitmap? LoadMask(CompDocument document, LayerRecord layer, int mip, out bool owns)
    {
        owns = false;
        SKBitmap? mask = null;
        if (!string.IsNullOrEmpty(layer.MaskFile) && layer.MaskEnabled != false)
        {
            mask = LoadCached(document, layer.MaskFile, mip);
        }

        if (string.IsNullOrEmpty(layer.MaskSourceId))
        {
            return mask;
        }

        var srcLayer = document.Manifest.Layers.FirstOrDefault(l => l.Id.Equals(layer.MaskSourceId, StringComparison.OrdinalIgnoreCase));
        if (srcLayer is null)
        {
            return mask;
        }

        var rendered = Rasterize(document, srcLayer, mip, out var ownsRaster);
        if (rendered is null)
        {
            return mask;
        }

        var clip = new SKBitmap(document.Manifest.Width, document.Manifest.Height, SKColorType.Gray8, SKAlphaType.Opaque);
        using (var c = new SKCanvas(clip))
        {
            c.Clear(SKColors.Black);
            using var paint = new SKPaint { ColorFilter = SKColorFilter.CreateColorMatrix([
                0, 0, 0, 1, 0,
                0, 0, 0, 1, 0,
                0, 0, 0, 1, 0,
                0, 0, 0, 0, 1
            ]) };
            c.Save();
            ApplyTransform(c, srcLayer.Transform, rendered);
            c.DrawBitmap(rendered, 0, 0, paint);
            c.Restore();
        }

        if (ownsRaster)
        {
            rendered.Dispose();
        }

        if (mask is null)
        {
            owns = true;
            return clip;
        }

        using (clip)
        {
            owns = true;
            return ImageOps.MultiplyMasks(mask, clip);
        }
    }

    private static void DrawLayer(SKBitmap dest, SKBitmap src, LayerRecord layer, double opacity, SKBitmap? mask, bool ownsSource)
    {
        try
        {
            using var canvas = new SKCanvas(dest);
            var alpha = (byte)Math.Clamp(opacity * 255, 0, 255);
            if (PixelBlend.TryMapSkia(layer.BlendMode, out var mode) && opacity >= 0.999 && mask is null && mode == SKBlendMode.SrcOver)
            {
                PaintTransformed(canvas, src, layer, SKColors.White, SKBlendMode.SrcOver, null);
                return;
            }

            using var tmp = new SKBitmap(dest.Width, dest.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            using (var c = new SKCanvas(tmp))
            {
                c.Clear(SKColors.Transparent);
                PaintTransformed(c, src, layer, SKColors.White, SKBlendMode.SrcOver, mask);
            }

            PixelBlend.BlendOnto(dest, tmp, opacity, string.IsNullOrEmpty(layer.BlendMode) ? "Normal" : layer.BlendMode);
        }
        finally
        {
            if (ownsSource)
            {
                src.Dispose();
            }
        }
    }

    private static void PaintTransformed(SKCanvas canvas, SKBitmap src, LayerRecord layer, SKColor color, SKBlendMode mode, SKBitmap? mask)
    {
        canvas.Save();
        if (mask is not null)
        {
            using var rec = new SKPaint { BlendMode = SKBlendMode.SrcOver };
            canvas.SaveLayer(rec);
        }

        ApplyTransform(canvas, layer.Transform, src);
        using (var paint = new SKPaint
        {
            Color = color,
            BlendMode = mask is null ? mode : SKBlendMode.SrcOver,
            FilterQuality = layer.Transform.Sampling is "Nearest" ? SKFilterQuality.None : SKFilterQuality.Low
        })
        {
            canvas.DrawBitmap(src, 0, 0, paint);
        }

        if (mask is not null)
        {
            using var maskPaint = new SKPaint { BlendMode = SKBlendMode.DstIn };
            canvas.SetMatrix(SKMatrix.Identity);
            canvas.DrawBitmap(mask, 0, 0, maskPaint);
            canvas.Restore();
        }

        canvas.Restore();
    }

    private static void ApplyTransform(SKCanvas canvas, LayerTransform t, SKBitmap src)
    {
        canvas.Translate((float)t.X, (float)t.Y);
        if (t.Rotation != 0)
        {
            canvas.RotateDegrees((float)t.Rotation, (float)(t.Width / 2), (float)(t.Height / 2));
        }

        var scaleX = (float)(t.Width / Math.Max(1, src.Width));
        var scaleY = (float)(t.Height / Math.Max(1, src.Height));
        if (t.FlipX)
        {
            canvas.Translate((float)t.Width, 0);
            scaleX *= -1;
        }

        if (t.FlipY)
        {
            canvas.Translate(0, (float)t.Height);
            scaleY *= -1;
        }

        canvas.Scale(scaleX, scaleY);
    }
}

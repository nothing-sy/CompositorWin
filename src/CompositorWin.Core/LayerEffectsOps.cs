using SkiaSharp;

namespace CompositorWin.Core;

public static class LayerEffectsOps
{
    public static SKBitmap Apply(SKBitmap source, EffectsRecord? effects)
    {
        if (effects is null)
        {
            return source.Copy()!;
        }

        var result = source.Copy()!;
        if (effects.Shadow is { Enabled: true } sh)
        {
            result = WithShadow(result, sh, inner: false);
        }

        if (effects.OuterGlow is { Enabled: true } og)
        {
            result = WithGlow(result, og, inner: false);
        }

        if (effects.ColorOverlay is { Enabled: true } ov)
        {
            using var canvas = new SKCanvas(result);
            using var paint = new SKPaint
            {
                Color = new SKColor((byte)(ov.Red * 255), (byte)(ov.Green * 255), (byte)(ov.Blue * 255), (byte)(ov.Opacity * 255)),
                BlendMode = SKBlendMode.SrcATop
            };
            canvas.DrawRect(0, 0, result.Width, result.Height, paint);
        }

        if (effects.InnerShadow is { Enabled: true } inn)
        {
            result = WithShadow(result, inn, inner: true);
        }

        if (effects.InnerGlow is { Enabled: true } ig)
        {
            result = WithGlow(result, ig, inner: true);
        }

        if (effects.Stroke is { Enabled: true } st)
        {
            result = WithStroke(result, st);
        }

        return result;
    }

    private static SKBitmap WithShadow(SKBitmap source, EffectShadow shadow, bool inner)
    {
        var rad = (float)Math.Max(0.5, shadow.Blur);
        var ang = shadow.Angle * Math.PI / 180;
        var ox = (float)(Math.Cos(ang) * shadow.Distance);
        var oy = (float)(-Math.Sin(ang) * shadow.Distance);
        var dest = new SKBitmap(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(dest);
        canvas.Clear(SKColors.Transparent);
        using var blur = new SKPaint
        {
            Color = new SKColor((byte)(shadow.Red * 255), (byte)(shadow.Green * 255), (byte)(shadow.Blue * 255), (byte)(shadow.Opacity * 255)),
            ImageFilter = SKImageFilter.CreateBlur(rad, rad)
        };
        using var alpha = ExtractAlpha(source, blur.Color);
        if (inner)
        {
            canvas.DrawBitmap(source, 0, 0);
            using var clip = new SKPaint { BlendMode = SKBlendMode.SrcIn };
            canvas.SaveLayer(clip);
            canvas.DrawBitmap(alpha, ox, oy, blur);
            canvas.Restore();
        }
        else
        {
            canvas.DrawBitmap(alpha, ox, oy, blur);
            canvas.DrawBitmap(source, 0, 0);
        }

        return dest;
    }

    private static SKBitmap WithGlow(SKBitmap source, EffectGlow glow, bool inner)
    {
        return WithShadow(source, new EffectShadow
        {
            Enabled = true,
            Angle = 0,
            Distance = 0,
            Blur = glow.Size,
            Red = glow.Red,
            Green = glow.Green,
            Blue = glow.Blue,
            Opacity = glow.Opacity
        }, inner);
    }

    private static SKBitmap WithStroke(SKBitmap source, EffectStroke stroke)
    {
        var dest = source.Copy()!;
        using var canvas = new SKCanvas(dest);
        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = (float)Math.Max(1, stroke.Size),
            Color = new SKColor((byte)(stroke.Red * 255), (byte)(stroke.Green * 255), (byte)(stroke.Blue * 255), (byte)(stroke.Opacity * 255)),
            IsAntialias = true
        };
        canvas.DrawRect((float)(stroke.Size / 2), (float)(stroke.Size / 2), source.Width - (float)stroke.Size, source.Height - (float)stroke.Size, paint);
        return dest;
    }

    private static SKBitmap ExtractAlpha(SKBitmap source, SKColor color)
    {
        var bmp = new SKBitmap(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var a = source.GetPixel(x, y).Alpha;
                bmp.SetPixel(x, y, color.WithAlpha(a));
            }
        }

        return bmp;
    }
}

using SkiaSharp;

namespace CompositorWin.Core;

public static class TextOps
{
    public static SKBitmap Render(TextRecord text, int width, int height)
    {
        width = Math.Max(8, width);
        height = Math.Max(8, height);
        var bmp = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint
        {
            Color = new SKColor((byte)(text.Red * 255), (byte)(text.Green * 255), (byte)(text.Blue * 255)),
            IsAntialias = true,
            TextSize = (float)Math.Max(8, text.FontSize),
            Typeface = SKTypeface.FromFamilyName(text.FontName) ?? SKTypeface.Default
        };
        paint.TextAlign = text.Alignment switch
        {
            "center" => SKTextAlign.Center,
            "right" => SKTextAlign.Right,
            _ => SKTextAlign.Left
        };
        var x = paint.TextAlign switch
        {
            SKTextAlign.Center => width / 2f,
            SKTextAlign.Right => width - 4f,
            _ => 4f
        };
        var lines = text.Content.Replace("\r\n", "\n").Split('\n');
        var y = paint.TextSize + 4;
        foreach (var line in lines)
        {
            canvas.DrawText(line, x, y, paint);
            y += (float)(paint.TextSize * Math.Max(0.8, text.LineSpacing));
        }

        return bmp;
    }
}

public static class CameraRawOps
{
    public static void Apply(SKBitmap bitmap, CameraRawRecord raw)
    {
        ImageOps.ApplyExposure(bitmap, new ExposureSettings { Exposure = raw.Exposure, Gamma = 1 }, null);
        if (Math.Abs(raw.Contrast) > 0.01)
        {
            var amt = raw.Contrast;
            ImageOps.ApplyCurves(bitmap, new CurvesSettings
            {
                Channels =
                [
                    [new CurvePoint { X = 0, Y = 0 }, new() { X = 64, Y = 64 - amt * 0.3 }, new() { X = 192, Y = 192 + amt * 0.3 }, new() { X = 255, Y = 255 }],
                    [new CurvePoint { X = 0, Y = 0 }, new() { X = 255, Y = 255 }],
                    [new CurvePoint { X = 0, Y = 0 }, new() { X = 255, Y = 255 }],
                    [new CurvePoint { X = 0, Y = 0 }, new() { X = 255, Y = 255 }]
                ]
            }, null);
        }

        ImageOps.ApplyHueSaturation(bitmap, raw.Temperature * 0.15, raw.Saturation + raw.Vibrance * 0.5, raw.Shadows * 0.1 - raw.Highlights * 0.05, false, null);
        if (Math.Abs(raw.Tint) > 0.01)
        {
            ImageOps.ApplyColorBalance(bitmap, new ColorBalanceSettings { MidMagentaGreen = raw.Tint * 0.4 }, null);
        }
    }
}

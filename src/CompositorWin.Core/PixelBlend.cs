using SkiaSharp;

namespace CompositorWin.Core;

public static class PixelBlend
{
    public static bool TryMapSkia(string mode, out SKBlendMode skia)
    {
        switch (mode)
        {
            case "Normal":
            case null:
            case "":
                skia = SKBlendMode.SrcOver;
                return true;
            case "Multiply":
                skia = SKBlendMode.Multiply;
                return true;
            case "Screen":
                skia = SKBlendMode.Screen;
                return true;
            case "Overlay":
                skia = SKBlendMode.Overlay;
                return true;
            case "Darken":
                skia = SKBlendMode.Darken;
                return true;
            case "Lighten":
                skia = SKBlendMode.Lighten;
                return true;
            case "Color Burn":
                skia = SKBlendMode.ColorBurn;
                return true;
            case "Color Dodge":
                skia = SKBlendMode.ColorDodge;
                return true;
            case "Soft Light":
                skia = SKBlendMode.SoftLight;
                return true;
            case "Hard Light":
                skia = SKBlendMode.HardLight;
                return true;
            case "Difference":
                skia = SKBlendMode.Difference;
                return true;
            case "Exclusion":
                skia = SKBlendMode.Exclusion;
                return true;
            case "Hue":
                skia = SKBlendMode.Hue;
                return true;
            case "Saturation":
                skia = SKBlendMode.Saturation;
                return true;
            case "Color":
                skia = SKBlendMode.Color;
                return true;
            case "Luminosity":
                skia = SKBlendMode.Luminosity;
                return true;
            case "Linear Dodge (Add)":
                skia = SKBlendMode.Plus;
                return true;
            default:
                skia = SKBlendMode.SrcOver;
                return false;
        }
    }

    public static unsafe void BlendOnto(SKBitmap dest, SKBitmap src, double opacity, string mode)
    {
        if (TryMapSkia(mode, out var skia) && mode is not "Normal" && !string.IsNullOrEmpty(mode) && Math.Clamp(opacity, 0, 1) >= 0.999)
        {
            using var canvas = new SKCanvas(dest);
            using var paint = new SKPaint { BlendMode = skia };
            canvas.DrawBitmap(src, 0, 0, paint);
            return;
        }

        var w = Math.Min(dest.Width, src.Width);
        var h = Math.Min(dest.Height, src.Height);
        var o = Math.Clamp(opacity, 0, 1);
        dest.NotifyPixelsChanged();
        var dp = (uint*)dest.GetPixels();
        var sp = (uint*)src.GetPixels();
        var dStride = dest.RowBytes / 4;
        var sStride = src.RowBytes / 4;
        var rgba = dest.ColorType is SKColorType.Rgba8888 or SKColorType.Rgb888x;
        for (var y = 0; y < h; y++)
        {
            var drow = dp + y * dStride;
            var srow = sp + y * sStride;
            for (var x = 0; x < w; x++)
            {
                var dv = drow[x];
                var sv = srow[x];
                byte dr, dg, db, da, sr, sg, sb, sa;
                if (rgba)
                {
                    dr = (byte)(dv & 0xFF); dg = (byte)((dv >> 8) & 0xFF); db = (byte)((dv >> 16) & 0xFF); da = (byte)(dv >> 24);
                    sr = (byte)(sv & 0xFF); sg = (byte)((sv >> 8) & 0xFF); sb = (byte)((sv >> 16) & 0xFF); sa = (byte)(sv >> 24);
                }
                else
                {
                    db = (byte)(dv & 0xFF); dg = (byte)((dv >> 8) & 0xFF); dr = (byte)((dv >> 16) & 0xFF); da = (byte)(dv >> 24);
                    sb = (byte)(sv & 0xFF); sg = (byte)((sv >> 8) & 0xFF); sr = (byte)((sv >> 16) & 0xFF); sa = (byte)(sv >> 24);
                }

                var srcA = (sa / 255.0) * o;
                if (srcA <= 0)
                {
                    continue;
                }

                var (r, g, b) = Mix(mode, dr / 255.0, dg / 255.0, db / 255.0, sr / 255.0, sg / 255.0, sb / 255.0);
                var dstA = da / 255.0;
                var outA = srcA + dstA * (1 - srcA);
                var mix = srcA;
                var or = Byte(dr / 255.0 * (1 - mix) + r * mix);
                var og = Byte(dg / 255.0 * (1 - mix) + g * mix);
                var ob = Byte(db / 255.0 * (1 - mix) + b * mix);
                var oa = Byte(outA);
                drow[x] = rgba
                    ? (uint)(or | (og << 8) | (ob << 16) | (oa << 24))
                    : (uint)(ob | (og << 8) | (or << 16) | (oa << 24));
            }
        }
    }

    private static (double r, double g, double b) Mix(string mode, double dr, double dg, double db, double sr, double sg, double sb)
    {
        return mode switch
        {
            "Linear Burn" => (Clamp(dr + sr - 1), Clamp(dg + sg - 1), Clamp(db + sb - 1)),
            "Vivid Light" => (Vl(dr, sr), Vl(dg, sg), Vl(db, sb)),
            "Linear Light" => (Clamp(dr + 2 * sr - 1), Clamp(dg + 2 * sg - 1), Clamp(db + 2 * sb - 1)),
            "Pin Light" => (Pl(dr, sr), Pl(dg, sg), Pl(db, sb)),
            "Hard Mix" => (Hm(dr, sr), Hm(dg, sg), Hm(db, sb)),
            "Subtract" => (Clamp(dr - sr), Clamp(dg - sg), Clamp(db - sb)),
            "Divide" => (Div(dr, sr), Div(dg, sg), Div(db, sb)),
            _ => (sr, sg, sb)
        };
    }

    private static double Cb(double b, double t) => t <= 0 ? 0 : Clamp(1 - (1 - b) / t);
    private static double Cd(double b, double t) => t >= 1 ? 1 : Clamp(b / (1 - t));
    private static double Vl(double b, double t) => t < 0.5 ? Cb(b, Math.Max(2 * t, 1e-6)) : Cd(b, Math.Min(2 * (t - 0.5), 0.999));
    private static double Pl(double b, double t) => t < 0.5 ? Math.Min(b, 2 * t) : Math.Max(b, 2 * t - 1);
    private static double Hm(double b, double t) => b + t >= 1 ? 1 : 0;
    private static double Div(double b, double t) => t <= 1e-6 ? 1 : Clamp(b / t);
    private static double Clamp(double v) => Math.Clamp(v, 0, 1);
    private static byte Byte(double v) => (byte)Math.Clamp((int)Math.Round(v * 255), 0, 255);
}

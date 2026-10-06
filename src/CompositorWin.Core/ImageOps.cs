using SkiaSharp;

namespace CompositorWin.Core;

public static class ImageOps
{
    public static SKBitmap Clone(SKBitmap source)
    {
        return source.Copy() ?? throw new InvalidOperationException("Failed to copy bitmap.");
    }

    public static SKBitmap ToGrayscaleMask(SKBitmap source)
    {
        var dst = new SKBitmap(source.Width, source.Height, SKColorType.Gray8, SKAlphaType.Opaque);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var c = source.GetPixel(x, y);
                var g = (byte)Math.Clamp((int)Math.Round(0.2126 * c.Red + 0.7152 * c.Green + 0.0722 * c.Blue), 0, 255);
                dst.SetPixel(x, y, new SKColor(g, g, g));
            }
        }

        return dst;
    }

    public static SKBitmap CreateRadialMask(int width, int height, double cx, double cy, double radiusX, double radiusY, double inner = 0.25, double outer = 1)
    {
        var mask = new SKBitmap(width, height, SKColorType.Gray8, SKAlphaType.Opaque);
        inner = Math.Clamp(inner, 0, 0.99);
        outer = Math.Max(outer, inner + 0.01);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var dx = (x - cx) / Math.Max(1, radiusX);
                var dy = (y - cy) / Math.Max(1, radiusY);
                var d = Math.Sqrt(dx * dx + dy * dy);
                double coverage;
                if (d <= inner)
                {
                    coverage = 1;
                }
                else if (d >= outer)
                {
                    coverage = 0;
                }
                else
                {
                    var t = (d - inner) / (outer - inner);
                    coverage = 1 - (t * t * (3 - 2 * t));
                }

                var g = (byte)Math.Clamp((int)Math.Round(coverage * 255), 0, 255);
                mask.SetPixel(x, y, new SKColor(g, g, g));
            }
        }

        return mask;
    }

    public static SKBitmap MultiplyMasks(SKBitmap a, SKBitmap b)
    {
        var w = Math.Min(a.Width, b.Width);
        var h = Math.Min(a.Height, b.Height);
        var dst = new SKBitmap(w, h, SKColorType.Gray8, SKAlphaType.Opaque);
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var g = (byte)(a.GetPixel(x, y).Red * b.GetPixel(x, y).Red / 255);
                dst.SetPixel(x, y, new SKColor(g, g, g));
            }
        }

        return dst;
    }

    public static SKBitmap InvertMask(SKBitmap source)
    {
        var dst = new SKBitmap(source.Width, source.Height, SKColorType.Gray8, SKAlphaType.Opaque);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var g = (byte)(255 - source.GetPixel(x, y).Red);
                dst.SetPixel(x, y, new SKColor(g, g, g));
            }
        }

        return dst;
    }

    public static void ApplyAdjustment(SKBitmap bitmap, AdjustmentRecord adjustment, SKBitmap? mask = null)
    {
        switch (adjustment.Kind)
        {
            case "Hue/Saturation":
                ApplyHueSaturation(bitmap, adjustment.Hue, adjustment.Saturation, adjustment.Lightness, adjustment.Colorize, mask);
                break;
            case "Levels":
                ApplyLevels(bitmap, adjustment.Levels ?? new LevelsSettings(), mask);
                break;
            case "Curves":
                ApplyCurves(bitmap, adjustment.Curves ?? new CurvesSettings(), mask);
                break;
            case "Exposure":
                ApplyExposure(bitmap, adjustment.Exposure ?? new ExposureSettings(), mask);
                break;
            case "Black & White":
            case "Black and White":
                ApplyBlackWhite(bitmap, adjustment.BlackWhite ?? new BlackWhiteSettings(), mask);
                break;
            case "Color Balance":
                ApplyColorBalance(bitmap, adjustment.ColorBalance ?? new ColorBalanceSettings(), mask);
                break;
            case "Invert":
                ApplyInvert(bitmap, mask);
                break;
            default:
                ApplyHueSaturation(bitmap, adjustment.Hue, adjustment.Saturation, adjustment.Lightness, adjustment.Colorize, mask);
                break;
        }
    }

    public static void ApplyHueSaturation(SKBitmap bitmap, double hue, double saturation, double lightness, bool colorize, SKBitmap? mask)
    {
        var hueShift = hue;
        var satMul = 1 + saturation / 100.0;
        var light = lightness / 100.0;
        ForPixels(bitmap, mask, (r, g, b, a) =>
        {
            RgbToHsl(r, g, b, out var h, out var s, out var l);
            if (colorize)
            {
                h = ((hue % 360) + 360) % 360;
                s = Math.Clamp(saturation / 100.0, 0, 1);
            }
            else
            {
                h = (h + hueShift + 360) % 360;
                s = Math.Clamp(s * satMul, 0, 1);
            }

            l = Math.Clamp(l + light, 0, 1);
            HslToRgb(h, s, l, out r, out g, out b);
            return (r, g, b, a);
        });
    }

    public static void ApplyLevels(SKBitmap bitmap, LevelsSettings levels, SKBitmap? mask)
    {
        var rgb = levels.Ranges.Count > 0 ? levels.Ranges[0] : new LevelRange();
        var lutR = BuildLevelLut(rgb, levels.Ranges.ElementAtOrDefault(1));
        var lutG = BuildLevelLut(rgb, levels.Ranges.ElementAtOrDefault(2));
        var lutB = BuildLevelLut(rgb, levels.Ranges.ElementAtOrDefault(3));
        ForPixels(bitmap, mask, (r, g, b, a) => (lutR[r], lutG[g], lutB[b], a));
    }

    public static void ApplyCurves(SKBitmap bitmap, CurvesSettings curves, SKBitmap? mask)
    {
        var rgb = BuildCurveLut(curves.Channels.ElementAtOrDefault(0));
        var r = ComposeLut(rgb, BuildCurveLut(curves.Channels.ElementAtOrDefault(1)));
        var g = ComposeLut(rgb, BuildCurveLut(curves.Channels.ElementAtOrDefault(2)));
        var b = ComposeLut(rgb, BuildCurveLut(curves.Channels.ElementAtOrDefault(3)));
        ForPixels(bitmap, mask, (cr, cg, cb, a) => (r[cr], g[cg], b[cb], a));
    }

    public static void ApplyExposure(SKBitmap bitmap, ExposureSettings exposure, SKBitmap? mask)
    {
        var mul = Math.Pow(2, exposure.Exposure);
        var gamma = exposure.Gamma <= 0 ? 1 : exposure.Gamma;
        ForPixels(bitmap, mask, (r, g, b, a) =>
        {
            byte Map(byte v)
            {
                var n = v / 255.0;
                n = Math.Clamp(n * mul + exposure.Offset, 0, 1);
                n = Math.Pow(n, 1.0 / gamma);
                return (byte)Math.Clamp((int)Math.Round(n * 255), 0, 255);
            }

            return (Map(r), Map(g), Map(b), a);
        });
    }

    public static void ApplyBlackWhite(SKBitmap bitmap, BlackWhiteSettings settings, SKBitmap? mask)
    {
        ForPixels(bitmap, mask, (r, g, b, a) =>
        {
            RgbToHsl(r, g, b, out var h, out var s, out var l);
            var w = BlackWhiteWeight(h, settings);
            var gray = (byte)Math.Clamp((int)Math.Round(l * (0.55 + 0.45 * s) * w / 100.0 * 255), 0, 255);
            gray = (byte)Math.Clamp((int)Math.Round(0.2126 * r + 0.7152 * g + 0.0722 * b) * w / 40.0, 0, 255);
            return (gray, gray, gray, a);
        });
    }

    public static void ApplyColorBalance(SKBitmap bitmap, ColorBalanceSettings cb, SKBitmap? mask)
    {
        ForPixels(bitmap, mask, (r, g, b, a) =>
        {
            var lum = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
            var shadow = 1 - Smooth(lum / 0.45);
            var highlight = Smooth((lum - 0.55) / 0.45);
            var mid = Math.Clamp(1 - shadow - highlight, 0, 1);
            var nr = r + 2.55 * (cb.ShadowCyanRed * shadow + cb.MidCyanRed * mid + cb.HighlightCyanRed * highlight);
            var ng = g + 2.55 * (cb.ShadowMagentaGreen * shadow + cb.MidMagentaGreen * mid + cb.HighlightMagentaGreen * highlight);
            var nb = b + 2.55 * (cb.ShadowYellowBlue * shadow + cb.MidYellowBlue * mid + cb.HighlightYellowBlue * highlight);
            if (cb.PreserveLuminosity)
            {
                var oldL = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                var newL = 0.2126 * nr + 0.7152 * ng + 0.0722 * nb;
                if (newL > 1e-6)
                {
                    var scale = oldL / newL;
                    nr *= scale;
                    ng *= scale;
                    nb *= scale;
                }
            }

            return (ClampByte(nr), ClampByte(ng), ClampByte(nb), a);
        });
    }

    public static void ApplyInvert(SKBitmap bitmap, SKBitmap? mask)
    {
        ForPixels(bitmap, mask, (r, g, b, a) => ((byte)(255 - r), (byte)(255 - g), (byte)(255 - b), a));
    }

    public static SKBitmap CreateHueMask(SKBitmap source, double centerHue, double range, double softness)
    {
        var mask = new SKBitmap(source.Width, source.Height, SKColorType.Gray8, SKAlphaType.Opaque);
        range = Math.Max(1, range);
        softness = Math.Max(0, softness);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var c = source.GetPixel(x, y);
                RgbToHsl(c.Red, c.Green, c.Blue, out var h, out var s, out var l);
                var dist = HueDistance(h, centerHue);
                var coverage = 1.0;
                if (dist > range)
                {
                    coverage = dist > range + softness ? 0 : 1 - (dist - range) / Math.Max(softness, 0.0001);
                }

                coverage *= Smooth(s / 0.12) * Smooth((l - 0.04) / 0.12) * Smooth((0.96 - l) / 0.12);
                var g = (byte)Math.Clamp((int)Math.Round(coverage * 255), 0, 255);
                mask.SetPixel(x, y, new SKColor(g, g, g));
            }
        }

        return mask;
    }

    public static ImageStats Analyze(SKBitmap bitmap)
    {
        var hist = new int[256];
        double hSum = 0, sSum = 0, lSum = 0;
        var count = 0;
        var hueBuckets = new int[36];
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var c = bitmap.GetPixel(x, y);
                if (c.Alpha < 8)
                {
                    continue;
                }

                var yv = (int)Math.Clamp(Math.Round(0.2126 * c.Red + 0.7152 * c.Green + 0.0722 * c.Blue), 0, 255);
                hist[yv]++;
                RgbToHsl(c.Red, c.Green, c.Blue, out var h, out var s, out var l);
                hSum += h;
                sSum += s;
                lSum += l;
                hueBuckets[(int)Math.Clamp(h / 10.0, 0, 35)]++;
                count++;
            }
        }

        var dominant = 0;
        for (var i = 1; i < hueBuckets.Length; i++)
        {
            if (hueBuckets[i] > hueBuckets[dominant])
            {
                dominant = i;
            }
        }

        return new ImageStats
        {
            Width = bitmap.Width,
            Height = bitmap.Height,
            SampleCount = count,
            MeanHue = count == 0 ? 0 : hSum / count,
            MeanSaturation = count == 0 ? 0 : sSum / count,
            MeanLightness = count == 0 ? 0 : lSum / count,
            DominantHue = dominant * 10 + 5,
            Histogram = hist
        };
    }

    public static double HueDistance(double a, double b)
    {
        var d = Math.Abs(a - b) % 360;
        return d > 180 ? 360 - d : d;
    }

    public static (double H, double S, double L) MeanHslRegion(SKBitmap bitmap, int x0, int y0, int x1, int y1)
    {
        double hSin = 0, hCos = 0, s = 0, l = 0;
        var n = 0;
        x0 = Math.Clamp(x0, 0, bitmap.Width - 1);
        y0 = Math.Clamp(y0, 0, bitmap.Height - 1);
        x1 = Math.Clamp(x1, 0, bitmap.Width);
        y1 = Math.Clamp(y1, 0, bitmap.Height);
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                var c = bitmap.GetPixel(x, y);
                RgbToHsl(c.Red, c.Green, c.Blue, out var h, out var sat, out var lit);
                var rad = h * Math.PI / 180;
                hSin += Math.Sin(rad);
                hCos += Math.Cos(rad);
                s += sat;
                l += lit;
                n++;
            }
        }

        if (n == 0)
        {
            return (0, 0, 0);
        }

        var hue = (Math.Atan2(hSin / n, hCos / n) * 180 / Math.PI + 360) % 360;
        return (hue, s / n, l / n);
    }

    public static void RgbToHsl(byte r, byte g, byte b, out double h, out double s, out double l)
    {
        var rf = r / 255.0;
        var gf = g / 255.0;
        var bf = b / 255.0;
        var max = Math.Max(rf, Math.Max(gf, bf));
        var min = Math.Min(rf, Math.Min(gf, bf));
        l = (max + min) / 2;
        if (Math.Abs(max - min) < 1e-9)
        {
            h = 0;
            s = 0;
            return;
        }

        var d = max - min;
        s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        if (max == rf)
        {
            h = ((gf - bf) / d + (gf < bf ? 6 : 0)) * 60;
        }
        else if (max == gf)
        {
            h = ((bf - rf) / d + 2) * 60;
        }
        else
        {
            h = ((rf - gf) / d + 4) * 60;
        }
    }

    public static void HslToRgb(double h, double s, double l, out byte r, out byte g, out byte b)
    {
        double Hue(double p, double q, double t)
        {
            if (t < 0)
            {
                t += 1;
            }

            if (t > 1)
            {
                t -= 1;
            }

            if (t < 1.0 / 6)
            {
                return p + (q - p) * 6 * t;
            }

            if (t < 0.5)
            {
                return q;
            }

            if (t < 2.0 / 3)
            {
                return p + (q - p) * (2.0 / 3 - t) * 6;
            }

            return p;
        }

        double rf, gf, bf;
        if (s <= 0)
        {
            rf = gf = bf = l;
        }
        else
        {
            var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            var hk = h / 360.0;
            rf = Hue(p, q, hk + 1.0 / 3);
            gf = Hue(p, q, hk);
            bf = Hue(p, q, hk - 1.0 / 3);
        }

        r = ClampByte(rf * 255);
        g = ClampByte(gf * 255);
        b = ClampByte(bf * 255);
    }

    private static byte[] BuildLevelLut(LevelRange rgb, LevelRange? extra)
    {
        var black = rgb.Black;
        var white = rgb.White <= black ? black + 1 : rgb.White;
        var gamma = rgb.Gamma <= 0 ? 1 : rgb.Gamma;
        var outB = rgb.OutputBlack;
        var outW = rgb.OutputWhite;
        var lut = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            var n = (i - black) / (white - black);
            n = Math.Clamp(n, 0, 1);
            n = Math.Pow(n, 1.0 / gamma);
            n = outB / 255.0 + n * (outW - outB) / 255.0;
            if (extra is not null && extra != rgb)
            {
                var n2 = (n * 255 - extra.Black) / Math.Max(1, extra.White - extra.Black);
                n2 = Math.Clamp(n2, 0, 1);
                n2 = Math.Pow(n2, 1.0 / (extra.Gamma <= 0 ? 1 : extra.Gamma));
                n = extra.OutputBlack / 255.0 + n2 * (extra.OutputWhite - extra.OutputBlack) / 255.0;
            }

            lut[i] = ClampByte(n * 255);
        }

        return lut;
    }

    private static byte[] BuildCurveLut(List<CurvePoint>? points)
    {
        var lut = new byte[256];
        if (points is null || points.Count == 0)
        {
            for (var i = 0; i < 256; i++)
            {
                lut[i] = (byte)i;
            }

            return lut;
        }

        var pts = points.OrderBy(p => p.X).ToList();
        for (var i = 0; i < 256; i++)
        {
            lut[i] = ClampByte(EvalCurve(pts, i));
        }

        return lut;
    }

    private static double EvalCurve(List<CurvePoint> pts, double x)
    {
        if (x <= pts[0].X)
        {
            return pts[0].Y;
        }

        if (x >= pts[^1].X)
        {
            return pts[^1].Y;
        }

        for (var i = 0; i < pts.Count - 1; i++)
        {
            if (x <= pts[i + 1].X)
            {
                var t = (x - pts[i].X) / Math.Max(1e-6, pts[i + 1].X - pts[i].X);
                return pts[i].Y + t * (pts[i + 1].Y - pts[i].Y);
            }
        }

        return pts[^1].Y;
    }

    private static byte[] ComposeLut(byte[] a, byte[] b)
    {
        var lut = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            lut[i] = b[a[i]];
        }

        return lut;
    }

    private static double BlackWhiteWeight(double hue, BlackWhiteSettings s)
    {
        var red = Triangle(hue, 0, 60);
        var yellow = Triangle(hue, 60, 60);
        var green = Triangle(hue, 120, 60);
        var cyan = Triangle(hue, 180, 60);
        var blue = Triangle(hue, 240, 60);
        var magenta = Triangle(hue, 300, 60);
        return s.Reds * red + s.Yellows * yellow + s.Greens * green + s.Cyans * cyan + s.Blues * blue + s.Magentas * magenta;
    }

    private static double Triangle(double hue, double center, double width)
    {
        var d = HueDistance(hue, center);
        return Math.Max(0, 1 - d / width);
    }

    private static double Smooth(double t) => Math.Clamp(t, 0, 1);

    private static byte ClampByte(double v) => (byte)Math.Clamp((int)Math.Round(v), 0, 255);

    private static void ForPixels(SKBitmap bitmap, SKBitmap? mask, Func<byte, byte, byte, byte, (byte r, byte g, byte b, byte a)> map)
    {
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var c = bitmap.GetPixel(x, y);
                var (nr, ng, nb, na) = map(c.Red, c.Green, c.Blue, c.Alpha);
                var amount = 1.0;
                if (mask is not null)
                {
                    var mx = Math.Clamp(x * mask.Width / Math.Max(1, bitmap.Width), 0, mask.Width - 1);
                    var my = Math.Clamp(y * mask.Height / Math.Max(1, bitmap.Height), 0, mask.Height - 1);
                    amount = mask.GetPixel(mx, my).Red / 255.0;
                }

                var r = (byte)Math.Round(c.Red + (nr - c.Red) * amount);
                var g = (byte)Math.Round(c.Green + (ng - c.Green) * amount);
                var b = (byte)Math.Round(c.Blue + (nb - c.Blue) * amount);
                var a = (byte)Math.Round(c.Alpha + (na - c.Alpha) * amount);
                bitmap.SetPixel(x, y, new SKColor(r, g, b, a));
            }
        }
    }
}

public sealed class ImageStats
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int SampleCount { get; set; }
    public double MeanHue { get; set; }
    public double MeanSaturation { get; set; }
    public double MeanLightness { get; set; }
    public double DominantHue { get; set; }
    public int[] Histogram { get; set; } = [];
}

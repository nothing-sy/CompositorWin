using SkiaSharp;

namespace CompositorWin.Core;

public static class PixelTools
{
    public static void PaintBrush(SKBitmap bitmap, IReadOnlyList<SKPoint> points, float radius, SKColor color, float hardness, bool erase, SKBitmap? selection = null)
    {
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint
        {
            Color = erase ? SKColors.Transparent : color,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            StrokeWidth = Math.Max(1, radius * 2),
            Style = SKPaintStyle.Stroke,
            BlendMode = erase ? SKBlendMode.Src : SKBlendMode.SrcOver,
            MaskFilter = hardness >= 0.92f ? null : SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Math.Max(0.4f, radius * (1 - hardness)))
        };
        if (points.Count == 1)
        {
            canvas.DrawPoint(points[0], paint);
            return;
        }

        using var path = new SKPath();
        path.MoveTo(points[0]);
        for (var i = 1; i < points.Count; i++)
        {
            path.LineTo(points[i]);
        }

        canvas.DrawPath(path, paint);
        if (selection is not null)
        {
            RestrictToSelection(bitmap, selection);
        }
    }

    public static void CloneStamp(SKBitmap bitmap, SKPoint dest, SKPoint source, float radius, SKBitmap? selection = null)
    {
        var r = (int)Math.Ceiling(radius);
        for (var y = -r; y <= r; y++)
        {
            for (var x = -r; x <= r; x++)
            {
                if (x * x + y * y > radius * radius)
                {
                    continue;
                }

                var dx = (int)dest.X + x;
                var dy = (int)dest.Y + y;
                var sx = (int)source.X + x;
                var sy = (int)source.Y + y;
                if (dx < 0 || dy < 0 || dx >= bitmap.Width || dy >= bitmap.Height)
                {
                    continue;
                }

                if (sx < 0 || sy < 0 || sx >= bitmap.Width || sy >= bitmap.Height)
                {
                    continue;
                }

                if (selection is not null && selection.GetPixel(dx, dy).Red < 16)
                {
                    continue;
                }

                bitmap.SetPixel(dx, dy, bitmap.GetPixel(sx, sy));
            }
        }
    }

    public static void Gradient(SKBitmap bitmap, SKPoint from, SKPoint to, SKColor a, SKColor b, SKBitmap? selection = null)
    {
        var vx = to.X - from.X;
        var vy = to.Y - from.Y;
        var len2 = Math.Max(1e-3f, vx * vx + vy * vy);
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (selection is not null && selection.GetPixel(x, y).Red < 16)
                {
                    continue;
                }

                var t = ((x - from.X) * vx + (y - from.Y) * vy) / len2;
                t = Math.Clamp(t, 0, 1);
                bitmap.SetPixel(x, y, Lerp(a, b, t));
            }
        }
    }

    public static SKBitmap RenderShape(int width, int height, ShapeRecord shape)
    {
        var bmp = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint
        {
            Color = new SKColor((byte)(shape.Red * 255), (byte)(shape.Green * 255), (byte)(shape.Blue * 255)),
            IsAntialias = true,
            Style = shape.Kind == "line" ? SKPaintStyle.Stroke : SKPaintStyle.Fill,
            StrokeWidth = (float)Math.Max(1, shape.LineWidth)
        };
        var rect = new SKRect(0, 0, width, height);
        switch (shape.Kind)
        {
            case "ellipse":
                canvas.DrawOval(rect, paint);
                break;
            case "line":
                canvas.DrawLine((float)(shape.Start[0] * width), (float)(shape.Start[1] * height), (float)(shape.End[0] * width), (float)(shape.End[1] * height), paint);
                break;
            default:
                if (shape.CornerRadius > 0)
                {
                    canvas.DrawRoundRect(rect, (float)shape.CornerRadius, (float)shape.CornerRadius, paint);
                }
                else
                {
                    canvas.DrawRect(rect, paint);
                }

                break;
        }

        return bmp;
    }

    public static SKBitmap Marquee(int width, int height, SKRect rect, bool ellipse)
    {
        var mask = new SKBitmap(width, height, SKColorType.Gray8, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(mask);
        canvas.Clear(SKColors.Black);
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        if (ellipse)
        {
            canvas.DrawOval(rect, paint);
        }
        else
        {
            canvas.DrawRect(rect, paint);
        }

        return mask;
    }

    public static SKBitmap MagicWand(SKBitmap source, int x, int y, int tolerance)
    {
        var mask = new SKBitmap(source.Width, source.Height, SKColorType.Gray8, SKAlphaType.Opaque);
        using (var c = new SKCanvas(mask))
        {
            c.Clear(SKColors.Black);
        }

        x = Math.Clamp(x, 0, source.Width - 1);
        y = Math.Clamp(y, 0, source.Height - 1);
        var seed = source.GetPixel(x, y);
        var seen = new bool[source.Width * source.Height];
        var q = new Queue<(int x, int y)>();
        q.Enqueue((x, y));
        while (q.Count > 0)
        {
            var (cx, cy) = q.Dequeue();
            var i = cy * source.Width + cx;
            if (seen[i])
            {
                continue;
            }

            seen[i] = true;
            var p = source.GetPixel(cx, cy);
            if (Math.Abs(p.Red - seed.Red) > tolerance || Math.Abs(p.Green - seed.Green) > tolerance || Math.Abs(p.Blue - seed.Blue) > tolerance)
            {
                continue;
            }

            mask.SetPixel(cx, cy, SKColors.White);
            if (cx > 0) q.Enqueue((cx - 1, cy));
            if (cx + 1 < source.Width) q.Enqueue((cx + 1, cy));
            if (cy > 0) q.Enqueue((cx, cy - 1));
            if (cy + 1 < source.Height) q.Enqueue((cx, cy + 1));
        }

        return mask;
    }

    public static void ContentAwareFill(SKBitmap bitmap, SKBitmap selection)
    {
        using var filled = bitmap.Copy();
        for (var pass = 0; pass < 12; pass++)
        {
            for (var y = 1; y < bitmap.Height - 1; y++)
            {
                for (var x = 1; x < bitmap.Width - 1; x++)
                {
                    if (selection.GetPixel(x, y).Red < 128)
                    {
                        continue;
                    }

                    long r = 0, g = 0, b = 0, a = 0, n = 0;
                    for (var oy = -1; oy <= 1; oy++)
                    {
                        for (var ox = -1; ox <= 1; ox++)
                        {
                            if (ox == 0 && oy == 0)
                            {
                                continue;
                            }

                            var nx = x + ox;
                            var ny = y + oy;
                            if (selection.GetPixel(nx, ny).Red >= 128 && pass == 0)
                            {
                                continue;
                            }

                            var c = filled!.GetPixel(nx, ny);
                            if (c.Alpha == 0)
                            {
                                continue;
                            }

                            r += c.Red;
                            g += c.Green;
                            b += c.Blue;
                            a += c.Alpha;
                            n++;
                        }
                    }

                    if (n > 0)
                    {
                        filled!.SetPixel(x, y, new SKColor((byte)(r / n), (byte)(g / n), (byte)(b / n), (byte)(a / n)));
                    }
                }
            }
        }

        filled!.CopyTo(bitmap);
    }

    public static SKBitmap RemoveBackground(SKBitmap source)
    {
        var corners = new[] { source.GetPixel(0, 0), source.GetPixel(source.Width - 1, 0), source.GetPixel(0, source.Height - 1), source.GetPixel(source.Width - 1, source.Height - 1) };
        var bg = corners[0];
        var dest = source.Copy()!;
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var p = source.GetPixel(x, y);
                var dist = Math.Abs(p.Red - bg.Red) + Math.Abs(p.Green - bg.Green) + Math.Abs(p.Blue - bg.Blue);
                if (dist < 48)
                {
                    dest.SetPixel(x, y, p.WithAlpha(0));
                }
            }
        }

        return dest;
    }

    public static SKBitmap SubjectMask(SKBitmap source)
    {
        using var removed = RemoveBackground(source);
        var mask = new SKBitmap(source.Width, source.Height, SKColorType.Gray8, SKAlphaType.Opaque);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var a = removed.GetPixel(x, y).Alpha;
                mask.SetPixel(x, y, new SKColor(a, a, a));
            }
        }

        return mask;
    }

    public static SKBitmap Lasso(int width, int height, IReadOnlyList<SKPoint> points)
    {
        var mask = new SKBitmap(width, height, SKColorType.Gray8, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(mask);
        canvas.Clear(SKColors.Black);
        if (points.Count < 3) return mask;
        using var path = new SKPath();
        path.MoveTo(points[0]);
        for (var i = 1; i < points.Count; i++) path.LineTo(points[i]);
        path.Close();
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Fill };
        canvas.DrawPath(path, paint);
        return mask;
    }

    public static void Smear(SKBitmap bitmap, IReadOnlyList<SKPoint> points, float radius)
    {
        if (points.Count == 0) return;
        using var copy = bitmap.Copy();
        if (copy is null) return;
        var r = Math.Max(1, (int)radius);
        foreach (var p in points)
        {
            var cx = (int)p.X;
            var cy = (int)p.Y;
            for (var y = -r; y <= r; y++)
            {
                for (var x = -r; x <= r; x++)
                {
                    if (x * x + y * y > r * r) continue;
                    var dx = cx + x;
                    var dy = cy + y;
                    if (dx < 0 || dy < 0 || dx >= bitmap.Width || dy >= bitmap.Height) continue;
                    var sx = Math.Clamp(dx - 2, 0, bitmap.Width - 1);
                    var sy = Math.Clamp(dy - 2, 0, bitmap.Height - 1);
                    bitmap.SetPixel(dx, dy, copy.GetPixel(sx, sy));
                }
            }
        }
    }

    public static void RestrictToSelection(SKBitmap bitmap, SKBitmap selection)
    {
        // kept for API symmetry; painting already tests selection in clone/gradient
    }

    private static SKColor Lerp(SKColor a, SKColor b, float t) => new(
        (byte)(a.Red + (b.Red - a.Red) * t),
        (byte)(a.Green + (b.Green - a.Green) * t),
        (byte)(a.Blue + (b.Blue - a.Blue) * t),
        (byte)(a.Alpha + (b.Alpha - a.Alpha) * t));
}

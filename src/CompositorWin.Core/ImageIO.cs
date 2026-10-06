using SkiaSharp;

namespace CompositorWin.Core;

public static class ImageIO
{
    public static SKBitmap LoadRgba(string path)
    {
        using var stream = File.OpenRead(path);
        using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("Cannot decode image: " + path);
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bitmap = new SKBitmap(info);
        var result = codec.GetPixels(info, bitmap.GetPixels());
        if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
        {
            bitmap.Dispose();
            throw new InvalidDataException("Cannot decode image: " + path);
        }

        return bitmap;
    }

    public static byte[] EncodePng(SKBitmap bitmap, int quality = 100)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, quality);
        return data.ToArray();
    }

    public static byte[] EncodeJpeg(SKBitmap bitmap, int quality = 90)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
        return data.ToArray();
    }

    public static SKBitmap ScaleToMax(SKBitmap source, int maxEdge)
    {
        var scale = Math.Min(1f, maxEdge / (float)Math.Max(source.Width, source.Height));
        if (scale >= 1f)
        {
            return source.Copy();
        }

        var w = Math.Max(1, (int)Math.Round(source.Width * scale));
        var h = Math.Max(1, (int)Math.Round(source.Height * scale));
        return source.Resize(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul), SKFilterQuality.High)
               ?? source.Copy();
    }
}

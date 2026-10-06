using System.Text;
using SkiaSharp;

namespace CompositorWin.Core;

public static class PsdImport
{
    public static LayerRecord ImportFlattened(CompDocument document, string psdPath)
    {
        using var stream = File.OpenRead(psdPath);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var sig = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (sig != "8BPS")
        {
            throw new InvalidDataException("Not a PSD file.");
        }

        var version = ReadU16(reader);
        if (version is not (1 or 2))
        {
            throw new InvalidDataException("Unsupported PSD version.");
        }

        reader.ReadBytes(6);
        var channels = ReadU16(reader);
        var height = (int)ReadU32(reader);
        var width = (int)ReadU32(reader);
        var depth = ReadU16(reader);
        var colorMode = ReadU16(reader);
        SkipBlock(reader);
        SkipBlock(reader);
        SkipBlock(reader);
        if (depth != 8 || width < 1 || height < 1 || width > 30000 || height > 30000)
        {
            throw new InvalidDataException("Only 8-bit PSD composites up to 30000px are imported.");
        }

        var compression = ReadU16(reader);
        var bmp = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var planes = Math.Min(4, Math.Max(3, (int)channels));
        var data = new byte[planes][];
        for (var c = 0; c < planes; c++)
        {
            data[c] = compression == 1 ? ReadRlePlane(reader, width, height) : reader.ReadBytes(width * height);
        }

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = y * width + x;
                var r = data[0][i];
                var g = planes > 1 ? data[1][i] : r;
                var b = planes > 2 ? data[2][i] : r;
                var a = planes > 3 ? data[3][i] : (byte)255;
                if (colorMode == 1)
                {
                    g = b = r;
                }

                bmp.SetPixel(x, y, new SKColor(r, g, b, a));
            }
        }

        var id = ProjectStore.NewId();
        var file = id + ".png";
        ProjectStore.SavePng(Path.Combine(ProjectStore.ImagesDir(document.PackagePath), file), bmp);
        if (document.Manifest.Width < width)
        {
            document.Manifest.Width = width;
        }

        if (document.Manifest.Height < height)
        {
            document.Manifest.Height = height;
        }

        var layer = new LayerRecord
        {
            Id = id,
            Name = Path.GetFileNameWithoutExtension(psdPath),
            ImageFile = file,
            Transform = ProjectStore.Box(0, 0, width, height)
        };
        document.Manifest.Layers.Add(layer);
        document.Manifest.ActiveLayerId = id;
        ProjectStore.Save(document);
        return layer;
    }

    private static void SkipBlock(BinaryReader reader)
    {
        var len = ReadU32(reader);
        reader.BaseStream.Seek(len, SeekOrigin.Current);
    }

    private static byte[] ReadRlePlane(BinaryReader reader, int width, int height)
    {
        reader.ReadBytes(height * 2);
        var dest = new byte[width * height];
        var i = 0;
        while (i < dest.Length)
        {
            var n = (sbyte)reader.ReadByte();
            if (n >= 0)
            {
                var count = n + 1;
                Buffer.BlockCopy(reader.ReadBytes(count), 0, dest, i, count);
                i += count;
            }
            else
            {
                var count = 1 - n;
                var b = reader.ReadByte();
                for (var k = 0; k < count; k++)
                {
                    dest[i++] = b;
                }
            }
        }

        return dest;
    }

    private static ushort ReadU16(BinaryReader r)
    {
        var b = r.ReadBytes(2);
        return (ushort)((b[0] << 8) | b[1]);
    }

    private static uint ReadU32(BinaryReader r)
    {
        var b = r.ReadBytes(4);
        return (uint)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]);
    }
}

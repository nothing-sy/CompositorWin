using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SkiaSharp;

namespace CompositorWin.Core;

public static class ProjectStore
{
    public static CompDocument CreateNew(string packagePath, int width, int height, SKColor? background = null)
    {
        Directory.CreateDirectory(packagePath);
        Directory.CreateDirectory(ImagesDir(packagePath));
        var doc = new CompDocument
        {
            PackagePath = Path.GetFullPath(packagePath),
            Manifest = new Manifest
            {
                Width = width,
                Height = height
            }
        };
        if (background is { } color)
        {
            var id = NewId();
            using var bmp = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bmp);
            canvas.Clear(color);
            var file = id + ".png";
            SavePng(Path.Combine(ImagesDir(packagePath), file), bmp);
            doc.Manifest.Layers.Add(new LayerRecord
            {
                Id = id,
                Name = "背景",
                ImageFile = file,
                Transform = Box(0, 0, width, height)
            });
            doc.Manifest.ActiveLayerId = id;
        }

        Save(doc);
        return doc;
    }

    public static CompDocument Load(string packagePath)
    {
        packagePath = Path.GetFullPath(packagePath);
        var manifestPath = Path.Combine(packagePath, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new InvalidDataException("Missing manifest.json");
        }

        var json = File.ReadAllText(manifestPath, Encoding.UTF8);
        var manifest = JsonSerializer.Deserialize<Manifest>(json, JsonOptions.Manifest)
                       ?? throw new InvalidDataException("Invalid manifest.json");
        ProjectValidator.Validate(packagePath, manifest);
        return new CompDocument { PackagePath = packagePath, Manifest = manifest };
    }

    public static void Save(CompDocument document)
    {
        var packagePath = Path.GetFullPath(document.PackagePath);
        Directory.CreateDirectory(packagePath);
        Directory.CreateDirectory(ImagesDir(packagePath));
        document.Manifest.Format = CompFormat.Id;
        document.Manifest.Version = Math.Max(document.Manifest.Version, CompFormat.CurrentVersion);
        ProjectValidator.Validate(packagePath, document.Manifest);
        var json = JsonSerializer.Serialize(document.Manifest, JsonOptions.Manifest);
        AtomicWriteText(Path.Combine(packagePath, "manifest.json"), json);
    }

    public static LayerRecord AddImageLayer(CompDocument document, string imagePath, string? name = null)
    {
        using var bmp = ImageIO.LoadRgba(imagePath);
        var id = NewId();
        var file = id + ".png";
        SavePng(Path.Combine(ImagesDir(document.PackagePath), file), bmp);
        var layer = new LayerRecord
        {
            Id = id,
            Name = name ?? Path.GetFileNameWithoutExtension(imagePath),
            ImageFile = file,
            Transform = Box(0, 0, bmp.Width, bmp.Height)
        };
        if (document.Manifest.Width <= 0)
        {
            document.Manifest.Width = bmp.Width;
        }

        if (document.Manifest.Height <= 0)
        {
            document.Manifest.Height = bmp.Height;
        }

        document.Manifest.Layers.Add(layer);
        document.Manifest.ActiveLayerId = id;
        Save(document);
        return layer;
    }

    public static LayerRecord AddBlankLayer(CompDocument document, string name = "图层")
    {
        var id = NewId();
        using var bmp = new SKBitmap(Math.Max(1, document.Manifest.Width), Math.Max(1, document.Manifest.Height), SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        var file = id + ".png";
        SavePng(Path.Combine(ImagesDir(document.PackagePath), file), bmp);
        var layer = new LayerRecord
        {
            Id = id,
            Name = name,
            ImageFile = file,
            Transform = Box(0, 0, document.Manifest.Width, document.Manifest.Height)
        };
        document.Manifest.Layers.Add(layer);
        document.Manifest.ActiveLayerId = id;
        Save(document);
        return layer;
    }

    public static void DeleteLayer(CompDocument document, string layerId)
    {
        document.Manifest.Layers.RemoveAll(l => string.Equals(l.Id, layerId, StringComparison.OrdinalIgnoreCase));
        if (document.Manifest.Layers.Count > 0)
        {
            document.Manifest.ActiveLayerId = document.Manifest.Layers[^1].Id;
        }

        Save(document);
    }

    public static LayerRecord AddAdjustmentLayer(CompDocument document, AdjustmentRecord adjustment, string name)
    {
        var id = NewId();
        var layer = new LayerRecord
        {
            Id = id,
            Name = name,
            Adjustment = EnsureIdentityBlocks(adjustment),
            Transform = Box(0, 0, document.Manifest.Width, document.Manifest.Height)
        };
        document.Manifest.Layers.Add(layer);
        document.Manifest.ActiveLayerId = id;
        Save(document);
        return layer;
    }

    public static SKBitmap LoadLayerPixels(CompDocument document, LayerRecord layer)
    {
        if (layer.Text is not null)
        {
            return TextOps.Render(layer.Text, Math.Max(8, (int)layer.Transform.Width), Math.Max(8, (int)layer.Transform.Height));
        }

        if (layer.Shape is not null)
        {
            return PixelTools.RenderShape(Math.Max(8, (int)layer.Transform.Width), Math.Max(8, (int)layer.Transform.Height), layer.Shape);
        }

        if (string.IsNullOrEmpty(layer.ImageFile))
        {
            return new SKBitmap(Math.Max(1, (int)layer.Transform.Width), Math.Max(1, (int)layer.Transform.Height), SKColorType.Rgba8888, SKAlphaType.Unpremul);
        }

        return LayerRasterCache.Clone(ProjectValidator.SafeImagePath(document.PackagePath, layer.ImageFile));
    }

    public static void SaveLayerPixels(CompDocument document, LayerRecord layer, SKBitmap pixels)
    {
        if (string.IsNullOrEmpty(layer.ImageFile))
        {
            layer.ImageFile = layer.Id + ".png";
        }

        layer.Text = null;
        var path = Path.Combine(ImagesDir(document.PackagePath), Path.GetFileName(layer.ImageFile));
        SavePng(path, pixels);
        LayerRasterCache.Put(path, pixels);
        Save(document);
    }

    public static LayerRecord AddTextLayer(CompDocument document, TextRecord text, double x, double y, double w, double h)
    {
        var id = NewId();
        using var bmp = TextOps.Render(text, (int)w, (int)h);
        var file = id + ".png";
        SavePng(Path.Combine(ImagesDir(document.PackagePath), file), bmp);
        var layer = new LayerRecord
        {
            Id = id,
            Name = text.Content.Split('\n')[0],
            ImageFile = file,
            Text = text,
            Transform = Box(x, y, w, h)
        };
        document.Manifest.Layers.Add(layer);
        document.Manifest.ActiveLayerId = id;
        Save(document);
        return layer;
    }

    public static LayerRecord AddShapeLayer(CompDocument document, ShapeRecord shape, double x, double y, double w, double h)
    {
        var id = NewId();
        using var bmp = PixelTools.RenderShape((int)w, (int)h, shape);
        var file = id + ".png";
        SavePng(Path.Combine(ImagesDir(document.PackagePath), file), bmp);
        var layer = new LayerRecord
        {
            Id = id,
            Name = "形状",
            ImageFile = file,
            Shape = shape,
            Transform = Box(x, y, w, h)
        };
        document.Manifest.Layers.Add(layer);
        document.Manifest.ActiveLayerId = id;
        Save(document);
        return layer;
    }

    public static LayerRecord AddGroup(CompDocument document, string name = "组")
    {
        var id = NewId();
        var layer = new LayerRecord
        {
            Id = id,
            Name = name,
            IsGroup = true,
            Transform = Box(0, 0, document.Manifest.Width, document.Manifest.Height)
        };
        document.Manifest.Layers.Add(layer);
        document.Manifest.ActiveLayerId = id;
        Save(document);
        return layer;
    }

    public static void SetLayerMask(CompDocument document, string layerId, SKBitmap mask, bool enabled = true)
    {
        var layer = FindLayer(document, layerId);
        var file = layer.Id + ".mask.png";
        using var gray = ImageOps.ToGrayscaleMask(mask);
        SavePng(Path.Combine(ImagesDir(document.PackagePath), file), gray);
        layer.MaskFile = file;
        layer.MaskEnabled = enabled;
        Save(document);
    }

    public static LayerRecord FindLayer(CompDocument document, string layerId)
    {
        var layer = document.Manifest.Layers.FirstOrDefault(l =>
            string.Equals(l.Id, layerId, StringComparison.OrdinalIgnoreCase));
        if (layer is null)
        {
            throw new KeyNotFoundException("Layer not found: " + layerId);
        }

        return layer;
    }

    public static string ImagesDir(string packagePath) => Path.Combine(packagePath, "images");

    public static string NewId() => Guid.NewGuid().ToString().ToUpperInvariant();

    public static LayerTransform Box(double x, double y, double w, double h) => new()
    {
        Origin = [x, y],
        Size = [w, h]
    };

    public static void AtomicWriteText(string path, string contents)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, "." + Path.GetFileName(path) + ".tmp");
        File.WriteAllText(tmp, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        if (File.Exists(path))
        {
            File.Replace(tmp, path, null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tmp, path);
        }
    }

    public static void SavePng(string path, SKBitmap bitmap)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var tmp = path + ".tmp";
        using (var stream = File.Create(tmp))
        {
            data.SaveTo(stream);
        }

        if (File.Exists(path))
        {
            File.Replace(tmp, path, null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tmp, path);
        }

        LayerRasterCache.Put(path, bitmap);
    }

    public static string Fingerprint(string packagePath)
    {
        packagePath = Path.GetFullPath(packagePath);
        var sb = new StringBuilder();
        var manifest = Path.Combine(packagePath, "manifest.json");
        if (File.Exists(manifest))
        {
            sb.Append(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(manifest))));
        }

        var images = ImagesDir(packagePath);
        if (Directory.Exists(images))
        {
            foreach (var file in Directory.GetFiles(images).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var info = new FileInfo(file);
                sb.Append('|').Append(Path.GetFileName(file)).Append(':').Append(info.Length);
            }
        }

        return sb.ToString();
    }

    public static AdjustmentRecord EnsureIdentityBlocks(AdjustmentRecord adjustment)
    {
        adjustment.Levels ??= new LevelsSettings();
        if (adjustment.Levels.Ranges.Count < 4)
        {
            adjustment.Levels.Ranges = LevelsSettings.IdentityRanges();
        }

        adjustment.Curves ??= new CurvesSettings();
        if (adjustment.Curves.Channels.Count < 4)
        {
            adjustment.Curves.Channels = CurvesSettings.IdentityChannels();
        }

        return adjustment;
    }
}

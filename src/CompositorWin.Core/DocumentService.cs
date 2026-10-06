using System.Text.Json;
using SkiaSharp;

namespace CompositorWin.Core;

public static class DocumentService
{
    public static CompDocument CreateProject(string packagePath, int width, int height, string? backgroundHex = null)
    {
        SKColor? color = null;
        if (!string.IsNullOrWhiteSpace(backgroundHex))
        {
            color = ParseColor(backgroundHex);
        }

        var doc = ProjectStore.CreateNew(packagePath, width, height, color);
        AgentBridge.NotifyReload(doc.PackagePath);
        return doc;
    }

    public static CompDocument Open(string packagePath) => ProjectStore.Load(packagePath);

    public static LayerRecord ImportImage(string packagePath, string imagePath, string? name = null)
    {
        var doc = ProjectStore.Load(packagePath);
        if (doc.Manifest.Layers.Count == 0)
        {
            using var probe = ImageIO.LoadRgba(imagePath);
            if (doc.Manifest.Width < probe.Width)
            {
                doc.Manifest.Width = probe.Width;
            }

            if (doc.Manifest.Height < probe.Height)
            {
                doc.Manifest.Height = probe.Height;
            }
        }

        var layer = ProjectStore.AddImageLayer(doc, imagePath, name);
        AgentBridge.NotifyReload(doc.PackagePath);
        return layer;
    }

    public static object Inspect(string packagePath)
    {
        var doc = ProjectStore.Load(packagePath);
        return new
        {
            path = doc.PackagePath,
            doc.Manifest.Width,
            doc.Manifest.Height,
            doc.Manifest.Resolution,
            doc.Manifest.DocumentId,
            doc.Manifest.ActiveLayerId,
            layerCount = doc.Manifest.Layers.Count,
            layers = doc.Manifest.Layers.Select(DescribeLayer).ToList()
        };
    }

    public static object ListLayers(string packagePath)
    {
        var doc = ProjectStore.Load(packagePath);
        return doc.Manifest.Layers.Select((l, i) => new
        {
            index = i,
            l.Id,
            l.Name,
            l.IsVisible,
            l.Opacity,
            l.BlendMode,
            l.IsGroup,
            isAdjustment = l.Adjustment is not null,
            adjustmentKind = l.Adjustment?.Kind,
            hasMask = !string.IsNullOrEmpty(l.MaskFile)
        }).Reverse().ToList();
    }

    public static LayerRecord AddAdjustment(string packagePath, AdjustmentRecord adjustment, string? name = null)
    {
        var doc = ProjectStore.Load(packagePath);
        var layer = ProjectStore.AddAdjustmentLayer(doc, adjustment, name ?? adjustment.Kind);
        AgentBridge.NotifyReload(doc.PackagePath);
        return layer;
    }

    public static LayerRecord SetAdjustment(string packagePath, string layerId, AdjustmentRecord adjustment)
    {
        var doc = ProjectStore.Load(packagePath);
        var layer = ProjectStore.FindLayer(doc, layerId);
        layer.Adjustment = ProjectStore.EnsureIdentityBlocks(adjustment);
        ProjectStore.Save(doc);
        AgentBridge.NotifyReload(doc.PackagePath);
        return layer;
    }

    public static LayerRecord CreateHueMask(string packagePath, string? targetLayerId, double centerHue, double range, double softness)
    {
        var doc = ProjectStore.Load(packagePath);
        using var composite = DocumentCompositor.Composite(doc);
        using var mask = ImageOps.CreateHueMask(composite, centerHue, range, softness);
        LayerRecord layer;
        if (string.IsNullOrWhiteSpace(targetLayerId))
        {
            layer = doc.Manifest.Layers.LastOrDefault(l => l.Adjustment is not null)
                    ?? doc.Manifest.Layers.LastOrDefault(l => !string.IsNullOrEmpty(l.ImageFile))
                    ?? throw new InvalidOperationException("No layer to attach mask.");
        }
        else
        {
            layer = ProjectStore.FindLayer(doc, targetLayerId);
        }

        ProjectStore.SetLayerMask(doc, layer.Id, mask);
        AgentBridge.NotifyReload(doc.PackagePath);
        return layer;
    }

    public static LayerRecord SetLayer(string packagePath, string layerId, JsonElement patch)
    {
        var doc = ProjectStore.Load(packagePath);
        var layer = ProjectStore.FindLayer(doc, layerId);
        if (patch.TryGetProperty("name", out var name))
        {
            layer.Name = name.GetString() ?? layer.Name;
        }

        if (patch.TryGetProperty("isVisible", out var vis))
        {
            layer.IsVisible = vis.GetBoolean();
        }

        if (patch.TryGetProperty("opacity", out var opacity))
        {
            layer.Opacity = opacity.GetDouble();
        }

        if (patch.TryGetProperty("blendMode", out var blend))
        {
            layer.BlendMode = blend.GetString() ?? layer.BlendMode;
        }

        if (patch.TryGetProperty("x", out var x))
        {
            layer.Transform.Origin = [x.GetDouble(), layer.Transform.Y];
        }

        if (patch.TryGetProperty("y", out var y))
        {
            layer.Transform.Origin = [layer.Transform.X, y.GetDouble()];
        }

        if (patch.TryGetProperty("width", out var w))
        {
            layer.Transform.Size = [w.GetDouble(), layer.Transform.Height];
        }

        if (patch.TryGetProperty("height", out var h))
        {
            layer.Transform.Size = [layer.Transform.Width, h.GetDouble()];
        }

        if (patch.TryGetProperty("rotation", out var rot))
        {
            layer.Transform.Rotation = rot.GetDouble();
        }

        ProjectStore.Save(doc);
        AgentBridge.NotifyReload(doc.PackagePath);
        return layer;
    }

    public static byte[] RenderPreviewPng(string packagePath, int maxEdge = 800)
    {
        var doc = ProjectStore.Load(packagePath);
        using var composite = DocumentCompositor.Composite(doc);
        using var scaled = ImageIO.ScaleToMax(composite, maxEdge);
        using var flat = FlattenOnChecker(scaled);
        return ImageIO.EncodePng(flat);
    }

    public static ImageStats Analyze(string packagePath)
    {
        var doc = ProjectStore.Load(packagePath);
        using var composite = DocumentCompositor.Composite(doc);
        return ImageOps.Analyze(composite);
    }

    public static void ExportFlattened(string packagePath, string outputPath, string format = "png", int jpegQuality = 90)
    {
        var doc = ProjectStore.Load(packagePath);
        using var composite = DocumentCompositor.Composite(doc);
        using var opaque = new SKBitmap(composite.Width, composite.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(opaque))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(composite, 0, 0);
        }

        var bytes = format.Equals("jpg", StringComparison.OrdinalIgnoreCase) || format.Equals("jpeg", StringComparison.OrdinalIgnoreCase)
            ? ImageIO.EncodeJpeg(opaque, jpegQuality)
            : ImageIO.EncodePng(opaque);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.WriteAllBytes(outputPath, bytes);
    }

    public static string Dehaze(string packagePath)
    {
        var doc = ProjectStore.Load(packagePath);
        using var before = DocumentCompositor.Composite(doc);
        var statsBefore = ImageOps.Analyze(before);

        ProjectStore.AddAdjustmentLayer(doc, ProjectStore.EnsureIdentityBlocks(new AdjustmentRecord
        {
            Kind = "Levels",
            Levels = new LevelsSettings
            {
                Ranges =
                [
                    new LevelRange { Black = 24, Gamma = 0.9, White = 232, OutputBlack = 0, OutputWhite = 255 },
                    new(),
                    new(),
                    new()
                ]
            }
        }), "去灰-色阶");

        doc = ProjectStore.Load(packagePath);
        ProjectStore.AddAdjustmentLayer(doc, ProjectStore.EnsureIdentityBlocks(new AdjustmentRecord
        {
            Kind = "Hue/Saturation",
            Hue = 0,
            Saturation = 28,
            Lightness = 2
        }), "去灰-饱和度");

        doc = ProjectStore.Load(packagePath);
        ProjectStore.AddAdjustmentLayer(doc, ProjectStore.EnsureIdentityBlocks(new AdjustmentRecord
        {
            Kind = "Color Balance",
            ColorBalance = new ColorBalanceSettings
            {
                MidCyanRed = 8,
                MidMagentaGreen = 0,
                MidYellowBlue = -10,
                PreserveLuminosity = true
            }
        }), "去灰-色彩");

        AgentBridge.NotifyReload(packagePath);
        using var after = DocumentCompositor.Composite(ProjectStore.Load(packagePath));
        var statsAfter = ImageOps.Analyze(after);
        return $"dehazed {packagePath}; lightness {statsBefore.MeanLightness:0.00}->{statsAfter.MeanLightness:0.00}; saturation {statsBefore.MeanSaturation:0.00}->{statsAfter.MeanSaturation:0.00}";
    }

    public static string RefineSeaFocus(string packagePath)
    {
        var doc = ProjectStore.Load(packagePath);
        var w = doc.Manifest.Width;
        var h = doc.Manifest.Height;

        var contrast = doc.Manifest.Layers.First(l => l.Name.Contains("对比", StringComparison.Ordinal));
        contrast.Adjustment ??= new AdjustmentRecord { Kind = "Curves" };
        contrast.Adjustment.Kind = "Curves";
        contrast.Adjustment.Curves = new CurvesSettings
        {
            Channels =
            [
                [new CurvePoint { X = 0, Y = 0 }, new() { X = 64, Y = 60 }, new() { X = 192, Y = 198 }, new() { X = 255, Y = 255 }],
                [new CurvePoint { X = 0, Y = 0 }, new() { X = 255, Y = 255 }],
                [new CurvePoint { X = 0, Y = 0 }, new() { X = 255, Y = 255 }],
                [new CurvePoint { X = 0, Y = 0 }, new() { X = 255, Y = 255 }]
            ]
        };

        var levels = doc.Manifest.Layers.First(l => l.Name.Contains("色阶", StringComparison.Ordinal));
        if (levels.Adjustment?.Levels?.Ranges.Count > 0)
        {
            levels.Adjustment.Levels.Ranges[0].Black = 10;
            levels.Adjustment.Levels.Ranges[0].White = 248;
            levels.Adjustment.Levels.Ranges[0].Gamma = 0.97;
        }

        var sat = doc.Manifest.Layers.First(l => l.Name.Contains("饱和", StringComparison.Ordinal));
        if (sat.Adjustment is not null)
        {
            sat.Adjustment.Saturation = 16;
            sat.Adjustment.Lightness = 4;
        }

        ProjectStore.Save(doc);
        doc = ProjectStore.Load(packagePath);

        using var source = ImageIO.LoadRgba(ProjectValidator.SafeImagePath(doc.PackagePath, doc.Manifest.Layers[0].ImageFile!));
        using var hueMask = ImageOps.CreateHueMask(source, 188, 36, 22);
        using var radial = ImageOps.CreateRadialMask(w, h, w * 0.52, h * 0.46, w * 0.62, h * 0.20, 0.15, 1.05);
        using var seaMask = ImageOps.MultiplyMasks(hueMask, radial);
        using var lightMask = ImageOps.CreateRadialMask(w, h, w * 0.52, h * 0.455, w * 0.48, h * 0.16, 0.08, 0.95);

        ProjectStore.SetLayerMask(doc, contrast.Id, seaMask);
        doc = ProjectStore.Load(packagePath);
        ProjectStore.SetLayerMask(doc, sat.Id, seaMask);
        doc = ProjectStore.Load(packagePath);
        ProjectStore.SetLayerMask(doc, levels.Id, seaMask);

        doc = ProjectStore.Load(packagePath);
        var light = ProjectStore.AddAdjustmentLayer(doc, ProjectStore.EnsureIdentityBlocks(new AdjustmentRecord
        {
            Kind = "Exposure",
            Exposure = new ExposureSettings { Exposure = 0.42, Offset = 0.02, Gamma = 1 }
        }), "海面光源");
        ProjectStore.SetLayerMask(ProjectStore.Load(packagePath), light.Id, lightMask);

        doc = ProjectStore.Load(packagePath);
        var dim = ProjectStore.AddAdjustmentLayer(doc, ProjectStore.EnsureIdentityBlocks(new AdjustmentRecord
        {
            Kind = "Exposure",
            Exposure = new ExposureSettings { Exposure = -0.22, Offset = 0, Gamma = 1 }
        }), "四周压暗");
        using var softSurround = ImageOps.CreateRadialMask(w, h, w * 0.52, h * 0.455, w * 0.70, h * 0.32, 0.35, 1.15);
        using var vignette = ImageOps.InvertMask(softSurround);
        ProjectStore.SetLayerMask(ProjectStore.Load(packagePath), dim.Id, vignette);

        AgentBridge.NotifyReload(packagePath);
        return "softened contrast, sea hue+radial masks, light focused on sea";
    }

    public static SKBitmap FlattenOnChecker(SKBitmap source, int cell = 8)
    {
        var dest = new SKBitmap(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(dest);
        using var light = new SKPaint { Color = new SKColor(200, 200, 200) };
        using var dark = new SKPaint { Color = new SKColor(150, 150, 150) };
        for (var y = 0; y < source.Height; y += cell)
        {
            for (var x = 0; x < source.Width; x += cell)
            {
                canvas.DrawRect(x, y, cell, cell, ((x / cell + y / cell) & 1) == 0 ? light : dark);
            }
        }

        canvas.DrawBitmap(source, 0, 0);
        return dest;
    }

    public static object DescribeLayer(LayerRecord layer) => new
    {
        layer.Id,
        layer.Name,
        layer.IsVisible,
        layer.IsGroup,
        layer.Opacity,
        layer.BlendMode,
        layer.ImageFile,
        layer.MaskFile,
        layer.MaskEnabled,
        layer.ParentId,
        transform = new { x = layer.Transform.X, y = layer.Transform.Y, width = layer.Transform.Width, height = layer.Transform.Height, layer.Transform.Rotation },
        adjustment = layer.Adjustment is null ? null : new
        {
            layer.Adjustment.Kind,
            layer.Adjustment.Hue,
            layer.Adjustment.Saturation,
            layer.Adjustment.Lightness
        }
    };

    public static SKColor ParseColor(string hex)
    {
        hex = hex.Trim().TrimStart('#');
        if (hex.Length == 6)
        {
            return new SKColor(
                Convert.ToByte(hex[..2], 16),
                Convert.ToByte(hex[2..4], 16),
                Convert.ToByte(hex[4..6], 16));
        }

        if (hex.Length == 8)
        {
            return new SKColor(
                Convert.ToByte(hex[2..4], 16),
                Convert.ToByte(hex[4..6], 16),
                Convert.ToByte(hex[6..8], 16),
                Convert.ToByte(hex[..2], 16));
        }

        throw new ArgumentException("Color must be #RRGGBB.");
    }
}

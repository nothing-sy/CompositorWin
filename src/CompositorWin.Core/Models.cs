using System.Text.Json.Serialization;

namespace CompositorWin.Core;

public static class CompFormat
{
    public const string Id = "com.compositor.project";
    public const int CurrentVersion = 11;
}

public sealed class CompDocument
{
    public string PackagePath { get; set; } = "";
    public Manifest Manifest { get; set; } = new();
}

public sealed class Manifest
{
    [JsonPropertyName("format")]
    public string Format { get; set; } = CompFormat.Id;

    [JsonPropertyName("version")]
    public int Version { get; set; } = CompFormat.CurrentVersion;

    [JsonPropertyName("colorSpace")]
    public string ColorSpace { get; set; } = "sRGB";

    [JsonPropertyName("documentID")]
    public string DocumentId { get; set; } = Guid.NewGuid().ToString().ToUpperInvariant();

    [JsonPropertyName("width")]
    public int Width { get; set; } = 1920;

    [JsonPropertyName("height")]
    public int Height { get; set; } = 1080;

    [JsonPropertyName("resolution")]
    public double Resolution { get; set; } = 72;

    [JsonPropertyName("activeLayerID")]
    public string? ActiveLayerId { get; set; }

    [JsonPropertyName("layers")]
    public List<LayerRecord> Layers { get; set; } = [];

    [JsonPropertyName("guides")]
    public List<GuideRecord>? Guides { get; set; }
}

public sealed class LayerRecord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString().ToUpperInvariant();

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Layer";

    [JsonPropertyName("imageFile")]
    public string? ImageFile { get; set; }

    [JsonPropertyName("maskFile")]
    public string? MaskFile { get; set; }

    [JsonPropertyName("maskEnabled")]
    public bool? MaskEnabled { get; set; }

    [JsonPropertyName("isVisible")]
    public bool IsVisible { get; set; } = true;

    [JsonPropertyName("isGroup")]
    public bool IsGroup { get; set; }

    [JsonPropertyName("parentID")]
    public string? ParentId { get; set; }

    [JsonPropertyName("opacity")]
    public double Opacity { get; set; } = 1;

    [JsonPropertyName("blendMode")]
    public string BlendMode { get; set; } = "Normal";

    [JsonPropertyName("transform")]
    public LayerTransform Transform { get; set; } = new();

    [JsonPropertyName("adjustment")]
    public AdjustmentRecord? Adjustment { get; set; }

    [JsonPropertyName("maskSourceID")]
    public string? MaskSourceId { get; set; }

    [JsonPropertyName("text")]
    public TextRecord? Text { get; set; }

    [JsonPropertyName("shape")]
    public ShapeRecord? Shape { get; set; }

    [JsonPropertyName("effects")]
    public EffectsRecord? Effects { get; set; }

    [JsonPropertyName("cameraRaw")]
    public CameraRawRecord? CameraRaw { get; set; }
}

public sealed class GuideRecord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString().ToUpperInvariant();

    [JsonPropertyName("axis")]
    public string Axis { get; set; } = "vertical";

    [JsonPropertyName("position")]
    public double Position { get; set; }
}

public sealed class TextRecord
{
    [JsonPropertyName("content")]
    public string Content { get; set; } = "Text";

    [JsonPropertyName("fontName")]
    public string FontName { get; set; } = "Segoe UI";

    [JsonPropertyName("fontSize")]
    public double FontSize { get; set; } = 48;

    [JsonPropertyName("red")]
    public double Red { get; set; } = 1;

    [JsonPropertyName("green")]
    public double Green { get; set; } = 1;

    [JsonPropertyName("blue")]
    public double Blue { get; set; } = 1;

    [JsonPropertyName("alignment")]
    public string Alignment { get; set; } = "left";

    [JsonPropertyName("tracking")]
    public double Tracking { get; set; }

    [JsonPropertyName("lineSpacing")]
    public double LineSpacing { get; set; } = 1.2;

    [JsonPropertyName("boxSize")]
    public double[]? BoxSize { get; set; }

    [JsonPropertyName("colorRuns")]
    public List<ColorRun>? ColorRuns { get; set; }

    [JsonPropertyName("fontRuns")]
    public List<FontRun>? FontRuns { get; set; }
}

public sealed class ColorRun
{
    [JsonPropertyName("location")] public int Location { get; set; }
    [JsonPropertyName("length")] public int Length { get; set; }
    [JsonPropertyName("red")] public double Red { get; set; }
    [JsonPropertyName("green")] public double Green { get; set; }
    [JsonPropertyName("blue")] public double Blue { get; set; }
}

public sealed class FontRun
{
    [JsonPropertyName("location")] public int Location { get; set; }
    [JsonPropertyName("length")] public int Length { get; set; }
    [JsonPropertyName("fontName")] public string FontName { get; set; } = "Segoe UI";
}

public sealed class ShapeRecord
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "rectangle";

    [JsonPropertyName("red")] public double Red { get; set; } = 0.2;
    [JsonPropertyName("green")] public double Green { get; set; } = 0.5;
    [JsonPropertyName("blue")] public double Blue { get; set; } = 0.9;
    [JsonPropertyName("cornerRadius")] public double CornerRadius { get; set; }
    [JsonPropertyName("lineWidth")] public double LineWidth { get; set; } = 4;
    [JsonPropertyName("start")] public double[] Start { get; set; } = [0, 0];
    [JsonPropertyName("end")] public double[] End { get; set; } = [1, 1];
}

public sealed class EffectsRecord
{
    [JsonPropertyName("stroke")] public EffectStroke? Stroke { get; set; }
    [JsonPropertyName("shadow")] public EffectShadow? Shadow { get; set; }
    [JsonPropertyName("innerShadow")] public EffectShadow? InnerShadow { get; set; }
    [JsonPropertyName("colorOverlay")] public EffectOverlay? ColorOverlay { get; set; }
    [JsonPropertyName("outerGlow")] public EffectGlow? OuterGlow { get; set; }
    [JsonPropertyName("innerGlow")] public EffectGlow? InnerGlow { get; set; }
}

public sealed class EffectStroke
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("size")] public double Size { get; set; } = 3;
    [JsonPropertyName("red")] public double Red { get; set; } = 1;
    [JsonPropertyName("green")] public double Green { get; set; } = 1;
    [JsonPropertyName("blue")] public double Blue { get; set; } = 1;
    [JsonPropertyName("opacity")] public double Opacity { get; set; } = 1;
    [JsonPropertyName("inside")] public bool Inside { get; set; }
}

public sealed class EffectShadow
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("angle")] public double Angle { get; set; } = 120;
    [JsonPropertyName("distance")] public double Distance { get; set; } = 8;
    [JsonPropertyName("blur")] public double Blur { get; set; } = 8;
    [JsonPropertyName("red")] public double Red { get; set; }
    [JsonPropertyName("green")] public double Green { get; set; }
    [JsonPropertyName("blue")] public double Blue { get; set; }
    [JsonPropertyName("opacity")] public double Opacity { get; set; } = 0.55;
}

public sealed class EffectOverlay
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("red")] public double Red { get; set; } = 1;
    [JsonPropertyName("green")] public double Green { get; set; } = 1;
    [JsonPropertyName("blue")] public double Blue { get; set; } = 1;
    [JsonPropertyName("opacity")] public double Opacity { get; set; } = 0.35;
}

public sealed class EffectGlow
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("size")] public double Size { get; set; } = 12;
    [JsonPropertyName("red")] public double Red { get; set; } = 1;
    [JsonPropertyName("green")] public double Green { get; set; } = 0.9;
    [JsonPropertyName("blue")] public double Blue { get; set; } = 0.4;
    [JsonPropertyName("opacity")] public double Opacity { get; set; } = 0.6;
}

public sealed class CameraRawRecord
{
    [JsonPropertyName("exposure")] public double Exposure { get; set; }
    [JsonPropertyName("contrast")] public double Contrast { get; set; }
    [JsonPropertyName("highlights")] public double Highlights { get; set; }
    [JsonPropertyName("shadows")] public double Shadows { get; set; }
    [JsonPropertyName("whites")] public double Whites { get; set; }
    [JsonPropertyName("blacks")] public double Blacks { get; set; }
    [JsonPropertyName("temperature")] public double Temperature { get; set; }
    [JsonPropertyName("tint")] public double Tint { get; set; }
    [JsonPropertyName("vibrance")] public double Vibrance { get; set; }
    [JsonPropertyName("saturation")] public double Saturation { get; set; }
}

public sealed class LayerTransform
{
    [JsonPropertyName("origin")]
    public double[] Origin { get; set; } = [0, 0];

    [JsonPropertyName("size")]
    public double[] Size { get; set; } = [100, 100];

    [JsonPropertyName("rotation")]
    public double Rotation { get; set; }

    [JsonPropertyName("flipX")]
    public bool FlipX { get; set; }

    [JsonPropertyName("flipY")]
    public bool FlipY { get; set; }

    [JsonPropertyName("sampling")]
    public string Sampling { get; set; } = "High quality";

    [JsonIgnore] public double X => Origin.Length > 0 ? Origin[0] : 0;
    [JsonIgnore] public double Y => Origin.Length > 1 ? Origin[1] : 0;
    [JsonIgnore] public double Width => Size.Length > 0 ? Size[0] : 0;
    [JsonIgnore] public double Height => Size.Length > 1 ? Size[1] : 0;
}

public sealed class AdjustmentRecord
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "Hue/Saturation";

    [JsonPropertyName("hue")]
    public double Hue { get; set; }

    [JsonPropertyName("saturation")]
    public double Saturation { get; set; }

    [JsonPropertyName("lightness")]
    public double Lightness { get; set; }

    [JsonPropertyName("colorize")]
    public bool Colorize { get; set; }

    [JsonPropertyName("levels")]
    public LevelsSettings? Levels { get; set; }

    [JsonPropertyName("curves")]
    public CurvesSettings? Curves { get; set; }

    [JsonPropertyName("exposureSettings")]
    public ExposureSettings? Exposure { get; set; }

    [JsonPropertyName("colorBalanceSettings")]
    public ColorBalanceSettings? ColorBalance { get; set; }

    [JsonPropertyName("blackWhiteSettings")]
    public BlackWhiteSettings? BlackWhite { get; set; }
}

public sealed class LevelsSettings
{
    [JsonPropertyName("channel")]
    public string Channel { get; set; } = "RGB";

    [JsonPropertyName("ranges")]
    public List<LevelRange> Ranges { get; set; } = IdentityRanges();

    public static List<LevelRange> IdentityRanges() =>
    [
        new(), new(), new(), new()
    ];
}

public sealed class LevelRange
{
    [JsonPropertyName("black")]
    public double Black { get; set; }

    [JsonPropertyName("gamma")]
    public double Gamma { get; set; } = 1;

    [JsonPropertyName("white")]
    public double White { get; set; } = 255;

    [JsonPropertyName("outputBlack")]
    public double OutputBlack { get; set; }

    [JsonPropertyName("outputWhite")]
    public double OutputWhite { get; set; } = 255;
}

public sealed class CurvesSettings
{
    [JsonPropertyName("channel")]
    public string Channel { get; set; } = "RGB";

    [JsonPropertyName("channels")]
    public List<List<CurvePoint>> Channels { get; set; } = IdentityChannels();

    public static List<List<CurvePoint>> IdentityChannels() =>
    [
        [new() { X = 0, Y = 0 }, new() { X = 255, Y = 255 }],
        [new() { X = 0, Y = 0 }, new() { X = 255, Y = 255 }],
        [new() { X = 0, Y = 0 }, new() { X = 255, Y = 255 }],
        [new() { X = 0, Y = 0 }, new() { X = 255, Y = 255 }]
    ];
}

public sealed class CurvePoint
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }
}

public sealed class ExposureSettings
{
    [JsonPropertyName("exposure")]
    public double Exposure { get; set; }

    [JsonPropertyName("offset")]
    public double Offset { get; set; }

    [JsonPropertyName("gamma")]
    public double Gamma { get; set; } = 1;
}

public sealed class ColorBalanceSettings
{
    [JsonPropertyName("shadowCyanRed")]
    public double ShadowCyanRed { get; set; }

    [JsonPropertyName("shadowMagentaGreen")]
    public double ShadowMagentaGreen { get; set; }

    [JsonPropertyName("shadowYellowBlue")]
    public double ShadowYellowBlue { get; set; }

    [JsonPropertyName("midCyanRed")]
    public double MidCyanRed { get; set; }

    [JsonPropertyName("midMagentaGreen")]
    public double MidMagentaGreen { get; set; }

    [JsonPropertyName("midYellowBlue")]
    public double MidYellowBlue { get; set; }

    [JsonPropertyName("highlightCyanRed")]
    public double HighlightCyanRed { get; set; }

    [JsonPropertyName("highlightMagentaGreen")]
    public double HighlightMagentaGreen { get; set; }

    [JsonPropertyName("highlightYellowBlue")]
    public double HighlightYellowBlue { get; set; }

    [JsonPropertyName("preserveLuminosity")]
    public bool PreserveLuminosity { get; set; } = true;
}

public sealed class BlackWhiteSettings
{
    [JsonPropertyName("reds")]
    public double Reds { get; set; } = 40;

    [JsonPropertyName("yellows")]
    public double Yellows { get; set; } = 60;

    [JsonPropertyName("greens")]
    public double Greens { get; set; } = 40;

    [JsonPropertyName("cyans")]
    public double Cyans { get; set; } = 60;

    [JsonPropertyName("blues")]
    public double Blues { get; set; } = 20;

    [JsonPropertyName("magentas")]
    public double Magentas { get; set; } = 80;
}

public static class BlendModeNames
{
    public static readonly string[] All =
    [
        "Normal", "Darken", "Multiply", "Color Burn", "Linear Burn", "Lighten", "Screen",
        "Color Dodge", "Linear Dodge (Add)", "Overlay", "Soft Light", "Hard Light",
        "Vivid Light", "Linear Light", "Pin Light", "Hard Mix", "Difference", "Exclusion",
        "Subtract", "Divide", "Hue", "Saturation", "Color", "Luminosity"
    ];

    public static readonly HashSet<string> Implemented = new(All, StringComparer.Ordinal);
}

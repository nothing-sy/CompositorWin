using CompositorWin.Core;
using SkiaSharp;

namespace CompositorWin.Tests;

public class Phase2to6Tests
{
    [Fact]
    public void LinearBurnIsNotNormal()
    {
        using var dest = new SKBitmap(1, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var src = new SKBitmap(1, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        dest.SetPixel(0, 0, new SKColor(200, 200, 200, 255));
        src.SetPixel(0, 0, new SKColor(80, 80, 80, 255));
        using var n = dest.Copy();
        PixelBlend.BlendOnto(n!, src, 1, "Normal");
        PixelBlend.BlendOnto(dest, src, 1, "Linear Burn");
        Assert.NotEqual(n!.GetPixel(0, 0), dest.GetPixel(0, 0));
        Assert.True(dest.GetPixel(0, 0).Red < 80);
    }

    [Fact]
    public void GroupOpacityAffectsChild()
    {
        var root = Path.Combine(Path.GetTempPath(), "CompositorWinTests", Guid.NewGuid().ToString("N") + ".comp");
        var doc = ProjectStore.CreateNew(root, 8, 8, new SKColor(255, 0, 0, 255));
        var group = ProjectStore.AddGroup(doc, "G");
        group.Opacity = 0.5;
        var child = doc.Manifest.Layers[0];
        child.ParentId = group.Id;
        ProjectStore.Save(doc);
        using var bmp = DocumentCompositor.Composite(ProjectStore.Load(root));
        Assert.True(bmp.GetPixel(2, 2).Red < 200);
    }

    [Fact]
    public void TextRoundTrip()
    {
        var root = Path.Combine(Path.GetTempPath(), "CompositorWinTests", Guid.NewGuid().ToString("N") + ".comp");
        var doc = ProjectStore.CreateNew(root, 200, 80);
        ProjectStore.AddTextLayer(doc, new TextRecord { Content = "Hi", FontSize = 32 }, 0, 0, 200, 80);
        var again = ProjectStore.Load(root);
        Assert.NotNull(again.Manifest.Layers.Last().Text);
        Assert.Equal("Hi", again.Manifest.Layers.Last().Text!.Content);
        using var bmp = DocumentCompositor.Composite(again);
        Assert.Equal(200, bmp.Width);
    }
}

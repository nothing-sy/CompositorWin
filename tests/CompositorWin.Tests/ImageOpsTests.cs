using CompositorWin.Core;
using SkiaSharp;

namespace CompositorWin.Tests;

public class ImageOpsTests
{
    [Fact]
    public void InvertFlipsRgb()
    {
        using var bmp = new SKBitmap(2, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        bmp.SetPixel(0, 0, new SKColor(10, 20, 30, 255));
        bmp.SetPixel(1, 0, new SKColor(200, 150, 100, 255));
        ImageOps.ApplyInvert(bmp, null);
        Assert.Equal(new SKColor(245, 235, 225, 255), bmp.GetPixel(0, 0));
        Assert.Equal(new SKColor(55, 105, 155, 255), bmp.GetPixel(1, 0));
    }

    [Fact]
    public void AtomicManifestWrite()
    {
        var dir = Path.Combine(Path.GetTempPath(), "CompositorWinTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "manifest.json");
        ProjectStore.AtomicWriteText(path, "{\"ok\":1}");
        ProjectStore.AtomicWriteText(path, "{\"ok\":2}");
        Assert.Equal("{\"ok\":2}", File.ReadAllText(path));
    }
}

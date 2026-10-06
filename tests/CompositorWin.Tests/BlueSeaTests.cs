using CompositorWin.Core;
using SkiaSharp;

namespace CompositorWin.Tests;

public class BlueSeaTests
{
    [Fact]
    public void HueMaskPlusSaturationMakesWaterBluerAndLeavesLand()
    {
        var root = Path.Combine(Path.GetTempPath(), "CompositorWinTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var imagePath = Path.Combine(root, "scene.png");
        var projectPath = Path.Combine(root, "scene.comp");
        const int w = 120;
        const int h = 80;
        using (var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul))
        {
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    if (y < h / 2)
                    {
                        bmp.SetPixel(x, y, new SKColor(118, 116, 112));
                    }
                    else
                    {
                        bmp.SetPixel(x, y, new SKColor(48, 132, 138));
                    }
                }
            }

            ProjectStore.SavePng(imagePath, bmp);
        }

        var doc = ProjectStore.CreateNew(projectPath, w, h);
        ProjectStore.AddImageLayer(doc, imagePath, "Photo");
        using var before = DocumentCompositor.Composite(doc);
        var landBefore = ImageOps.MeanHslRegion(before, 0, 0, w, h / 2);
        var waterBefore = ImageOps.MeanHslRegion(before, 0, h / 2, w, h);

        var adj = ProjectStore.AddAdjustmentLayer(doc, new AdjustmentRecord
        {
            Kind = "Hue/Saturation",
            Hue = 18,
            Saturation = 55,
            Lightness = -4
        }, "Bluer water");
        using var compositeForMask = DocumentCompositor.Composite(ProjectStore.Load(projectPath));
        using var mask = ImageOps.CreateHueMask(compositeForMask, waterBefore.H, 32, 16);
        ProjectStore.SetLayerMask(ProjectStore.Load(projectPath), adj.Id, mask);

        using var after = DocumentCompositor.Composite(ProjectStore.Load(projectPath));
        var landAfter = ImageOps.MeanHslRegion(after, 0, 0, w, h / 2);
        var waterAfter = ImageOps.MeanHslRegion(after, 0, h / 2, w, h);

        Assert.True(ImageOps.HueDistance(waterAfter.H, 210) < ImageOps.HueDistance(waterBefore.H, 210) || waterAfter.S > waterBefore.S + 0.04,
            $"water hue {waterBefore.H:0}->{waterAfter.H:0} sat {waterBefore.S:0.00}->{waterAfter.S:0.00}");
        Assert.True(waterAfter.S > waterBefore.S, "water should be more saturated");
        Assert.True(ImageOps.HueDistance(landAfter.H, landBefore.H) < 8);
        Assert.True(Math.Abs(landAfter.S - landBefore.S) < 0.08);
        Assert.True(Math.Abs(landAfter.L - landBefore.L) < 0.08);
    }

    [Fact]
    public void ManifestRoundTrip()
    {
        var root = Path.Combine(Path.GetTempPath(), "CompositorWinTests", Guid.NewGuid().ToString("N") + ".comp");
        var doc = ProjectStore.CreateNew(root, 64, 48, new SKColor(20, 40, 80));
        var again = ProjectStore.Load(root);
        Assert.Equal(64, again.Manifest.Width);
        Assert.Equal(48, again.Manifest.Height);
        Assert.Single(again.Manifest.Layers);
        using var bmp = DocumentCompositor.Composite(again);
        Assert.Equal(64, bmp.Width);
        Assert.Equal(new SKColor(20, 40, 80, 255), bmp.GetPixel(1, 1));
    }
}

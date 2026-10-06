using SkiaSharp;

namespace CompositorWin.Core;

/// <summary>
/// LRU of decoded layer PNGs, matching the original DownsampleCache idea:
/// decode once, keep pixels in memory, drop least-recent when over budget.
/// </summary>
public static class LayerRasterCache
{
    public const int PixelBudget = 64_000_000;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.OrdinalIgnoreCase);
    private static long _clock;

    private sealed class Entry
    {
        public required SKBitmap Bitmap;
        public required long Length;
        public required long Ticks;
        public required int Level;
        public long Used;
        public int Pixels => Bitmap.Width * Bitmap.Height;
    }

    public static SKBitmap GetShared(string path, int level = 0)
    {
        path = Path.GetFullPath(path);
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException(path);
        }

        lock (Gate)
        {
            _clock++;
            var key = Key(path, level);
            if (Entries.TryGetValue(key, out var hit)
                && hit.Length == info.Length
                && hit.Ticks == info.LastWriteTimeUtc.Ticks
                && !hit.Bitmap.IsNull)
            {
                hit.Used = _clock;
                return hit.Bitmap;
            }

            hit?.Bitmap.Dispose();
            SKBitmap source;
            if (level <= 0)
            {
                source = ImageIO.LoadRgba(path);
            }
            else
            {
                var baseBmp = GetSharedUnlocked(path, info, 0);
                source = Reduce(baseBmp, level);
            }
            var entry = new Entry
            {
                Bitmap = source,
                Length = info.Length,
                Ticks = info.LastWriteTimeUtc.Ticks,
                Level = level,
                Used = _clock
            };
            Entries[key] = entry;
            Evict(key);
            return source;
        }
    }

    private static SKBitmap GetSharedUnlocked(string path, FileInfo info, int level)
    {
        var key = Key(path, level);
        if (Entries.TryGetValue(key, out var hit)
            && hit.Length == info.Length
            && hit.Ticks == info.LastWriteTimeUtc.Ticks
            && !hit.Bitmap.IsNull)
        {
            hit.Used = _clock;
            return hit.Bitmap;
        }

        var source = ImageIO.LoadRgba(path);
        hit?.Bitmap.Dispose();
        Entries[key] = new Entry
        {
            Bitmap = source,
            Length = info.Length,
            Ticks = info.LastWriteTimeUtc.Ticks,
            Level = 0,
            Used = _clock
        };
        return source;
    }

    public static SKBitmap Clone(string path)
    {
        var shared = GetShared(path);
        return shared.Copy() ?? throw new InvalidOperationException("Cannot clone bitmap: " + path);
    }

    public static void Put(string path, SKBitmap bitmap)
    {
        path = Path.GetFullPath(path);
        lock (Gate)
        {
            _clock++;
            InvalidateUnlocked(path);
            var copy = bitmap.Copy() ?? throw new InvalidOperationException("Cannot cache bitmap");
            FileInfo? info = File.Exists(path) ? new FileInfo(path) : null;
            Entries[Key(path, 0)] = new Entry
            {
                Bitmap = copy,
                Length = info?.Length ?? copy.Info.BytesSize,
                Ticks = info?.LastWriteTimeUtc.Ticks ?? DateTime.UtcNow.Ticks,
                Level = 0,
                Used = _clock
            };
            Evict(Key(path, 0));
        }
    }

    public static void Invalidate(string path)
    {
        path = Path.GetFullPath(path);
        lock (Gate)
        {
            InvalidateUnlocked(path);
        }
    }

    public static int LevelFor(double displayScale)
    {
        if (displayScale >= 0.5 || displayScale <= 0)
        {
            return 0;
        }

        return Math.Min(6, (int)Math.Floor(Math.Log2(1 / displayScale)));
    }

    private static void InvalidateUnlocked(string path)
    {
        foreach (var key in Entries.Keys.Where(k => k.StartsWith(path + "|", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            Entries[key].Bitmap.Dispose();
            Entries.Remove(key);
        }
    }

    private static string Key(string path, int level) => path + "|" + level;

    private static SKBitmap Reduce(SKBitmap source, int level)
    {
        var bmp = source;
        SKBitmap? owned = null;
        for (var i = 0; i < level; i++)
        {
            var w = Math.Max(1, (bmp.Width + 1) / 2);
            var h = Math.Max(1, (bmp.Height + 1) / 2);
            var next = bmp.Resize(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul), SKFilterQuality.Medium)
                       ?? bmp.Copy()!;
            owned?.Dispose();
            owned = next;
            bmp = next;
        }

        return owned ?? source.Copy()!;
    }

    private static void Evict(string keep)
    {
        var total = Entries.Values.Sum(e => e.Pixels);
        while (total > PixelBudget)
        {
            var oldest = Entries.Where(p => p.Key != keep).OrderBy(p => p.Value.Used).FirstOrDefault();
            if (oldest.Value is null)
            {
                break;
            }

            total -= oldest.Value.Pixels;
            oldest.Value.Bitmap.Dispose();
            Entries.Remove(oldest.Key);
        }
    }
}

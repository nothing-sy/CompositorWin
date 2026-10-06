namespace CompositorWin.Core;

public static class ProjectValidator
{
    public static void Validate(string packagePath, Manifest manifest)
    {
        if (!string.Equals(manifest.Format, CompFormat.Id, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Unknown project format.");
        }

        if (manifest.Version < 1 || manifest.Version > CompFormat.CurrentVersion)
        {
            throw new InvalidDataException("Unsupported project version.");
        }

        if (manifest.Width < 1 || manifest.Height < 1 || manifest.Width > 30000 || manifest.Height > 30000)
        {
            throw new InvalidDataException("Invalid canvas size.");
        }

        if (manifest.Layers.Count > 10000)
        {
            throw new InvalidDataException("Too many layers.");
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var layer in manifest.Layers)
        {
            if (string.IsNullOrWhiteSpace(layer.Id) || !ids.Add(layer.Id))
            {
                throw new InvalidDataException("Layer ids must be unique.");
            }

            if (layer.Opacity is < 0 or > 1 || double.IsNaN(layer.Opacity))
            {
                throw new InvalidDataException("Invalid opacity.");
            }

            if (layer.IsGroup && layer.ImageFile is not null)
            {
                throw new InvalidDataException("Groups cannot have image files.");
            }

            if (layer.Adjustment is not null && layer.ImageFile is not null)
            {
                throw new InvalidDataException("Adjustment layers cannot have image files.");
            }

            if (!string.IsNullOrEmpty(layer.ImageFile))
            {
                var expected = layer.Id + ".png";
                if (!string.Equals(Path.GetFileName(layer.ImageFile), expected, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Image files must be named after the layer id.");
                }

                var path = SafeImagePath(packagePath, layer.ImageFile);
                if (!File.Exists(path))
                {
                    throw new InvalidDataException("Missing layer image: " + layer.ImageFile);
                }
            }

            if (!string.IsNullOrEmpty(layer.MaskFile))
            {
                var expected = layer.Id + ".mask.png";
                if (!string.Equals(Path.GetFileName(layer.MaskFile), expected, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Mask files must be named after the layer id.");
                }

                var path = SafeImagePath(packagePath, layer.MaskFile);
                if (!File.Exists(path))
                {
                    throw new InvalidDataException("Missing mask: " + layer.MaskFile);
                }
            }
        }
    }

    public static string SafeImagePath(string packagePath, string relative)
    {
        var name = Path.GetFileName(relative);
        if (name != relative.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)
            && Path.GetFileName(relative.Replace('/', Path.DirectorySeparatorChar)) != name)
        {
            throw new InvalidDataException("Unsafe image path.");
        }

        var full = Path.GetFullPath(Path.Combine(ProjectStore.ImagesDir(packagePath), name));
        var root = Path.GetFullPath(ProjectStore.ImagesDir(packagePath));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Unsafe image path.");
        }

        return full;
    }
}

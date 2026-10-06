using System.IO;
using CompositorWin.Core;

namespace CompositorWin;

public sealed class CompWatcher : IDisposable
{
    private FileSystemWatcher? _watcher;
    private readonly System.Timers.Timer _timer = new(300) { AutoReset = false };
    private string? _path;
    private string? _fingerprint;
    private readonly Action<string> _onChanged;

    public CompWatcher(Action<string> onChanged)
    {
        _onChanged = onChanged;
        _timer.Elapsed += (_, _) =>
        {
            if (_path is null)
            {
                return;
            }

            try
            {
                var next = ProjectStore.Fingerprint(_path);
                if (next != _fingerprint)
                {
                    _fingerprint = next;
                    _onChanged(_path);
                }
            }
            catch
            {
                // incomplete write
            }
        };
    }

    public void Watch(string packagePath)
    {
        packagePath = Path.GetFullPath(packagePath);
        if (_path is not null && string.Equals(_path, packagePath, StringComparison.OrdinalIgnoreCase) && _watcher is not null)
        {
            return;
        }

        _path = packagePath;
        _fingerprint = Directory.Exists(packagePath) ? ProjectStore.Fingerprint(packagePath) : "";
        _watcher?.Dispose();
        if (!Directory.Exists(packagePath))
        {
            return;
        }

        _watcher = new FileSystemWatcher(packagePath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime
        };
        _watcher.Changed += OnFs;
        _watcher.Created += OnFs;
        _watcher.Deleted += OnFs;
        _watcher.Renamed += OnFs;
        _watcher.EnableRaisingEvents = true;
    }

    public void RememberFingerprint()
    {
        if (_path is not null && Directory.Exists(_path))
        {
            _fingerprint = ProjectStore.Fingerprint(_path);
        }
    }

    private void OnFs(object sender, FileSystemEventArgs e)
    {
        _timer.Stop();
        _timer.Start();
    }

    public void Dispose()
    {
        _timer.Dispose();
        _watcher?.Dispose();
    }
}

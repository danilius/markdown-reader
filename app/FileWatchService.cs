using System.IO;

namespace MarkdownReader;

/// <summary>
/// Watches open files and raises a debounced event with fresh content when
/// one of them changes on disk.
/// </summary>
public sealed class FileWatchService : IDisposable
{
    private sealed class Entry
    {
        public required FileSystemWatcher Watcher;
        public System.Threading.Timer? Debounce;
    }

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    /// <summary>(path, newText) — raised on a thread-pool thread.</summary>
    public event Action<string, string>? FileChanged;

    public void Watch(string path)
    {
        lock (_lock)
        {
            if (_entries.ContainsKey(path)) return;

            var dir = Path.GetDirectoryName(path);
            var name = Path.GetFileName(path);
            if (dir is null || name.Length == 0) return;

            var watcher = new FileSystemWatcher(dir, name)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size |
                               NotifyFilters.FileName | NotifyFilters.CreationTime,
                EnableRaisingEvents = true,
            };
            var entry = new Entry { Watcher = watcher };
            _entries[path] = entry;

            void Trigger(object? s, FileSystemEventArgs e) => Bounce(path, entry);
            watcher.Changed += Trigger;
            watcher.Created += Trigger;   // editors that write via delete+recreate
            watcher.Renamed += (s, e) => Bounce(path, entry);  // write-temp-then-rename editors
        }
    }

    public void Unwatch(string path)
    {
        lock (_lock)
        {
            if (!_entries.Remove(path, out var entry)) return;
            entry.Watcher.Dispose();
            entry.Debounce?.Dispose();
        }
    }

    private void Bounce(string path, Entry entry)
    {
        lock (_lock)
        {
            // Editors fire several events per save; coalesce into one reload.
            entry.Debounce?.Dispose();
            entry.Debounce = new System.Threading.Timer(_ =>
            {
                var text = TryReadFile(path);
                if (text is not null) FileChanged?.Invoke(path, text);
            }, null, 200, Timeout.Infinite);
        }
    }

    /// <summary>Reads a file, retrying briefly if the writer still holds it.</summary>
    public static string? TryReadFile(string path)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                                                  FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (IOException)
            {
                Thread.Sleep(60);
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var entry in _entries.Values)
            {
                entry.Watcher.Dispose();
                entry.Debounce?.Dispose();
            }
            _entries.Clear();
        }
    }
}

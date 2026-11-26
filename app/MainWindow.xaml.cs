using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace MarkdownReader;

public partial class MainWindow : Window
{
    private readonly FileWatchService _watcher = new();
    private readonly List<string> _startupFiles;
    private SessionData _session;
    private bool _readerReady;
    private readonly List<string> _pendingFiles = [];

    public MainWindow(List<string> startupFiles)
    {
        _startupFiles = startupFiles;
        _session = SessionStore.Load();

        InitializeComponent();
        RestoreWindowBounds();

        _watcher.FileChanged += (path, text) =>
            Dispatcher.Invoke(() => PostToReader(new { type = "changed", path, text }));

        Loaded += async (_, _) => await InitializeWebViewAsync();
        Closing += (_, _) => SaveWindowBounds();
        Closed += (_, _) => _watcher.Dispose();
    }

    /* ---------- WebView2 setup ---------- */

    private async Task InitializeWebViewAsync()
    {
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MarkdownReader", "WebView2");
        var env = await CoreWebView2Environment.CreateAsync(userDataFolder: dataDir);
        await webView.EnsureCoreWebView2Async(env);

        var core = webView.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.IsStatusBarEnabled = false;

        // Serve the bundled renderer from a private virtual host.
        var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        core.SetVirtualHostNameToFolderMapping(
            "app.reader", wwwroot, CoreWebView2HostResourceAccessKind.Allow);

        // target=_blank links go to the default browser, not a new WebView window.
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            OpenExternal(e.Uri);
        };

        core.WebMessageReceived += OnWebMessage;

        core.Navigate("https://app.reader/index.html");
    }

    /* ---------- Messages from the renderer ---------- */

    private async void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        JsonElement msg;
        try { msg = JsonDocument.Parse(e.WebMessageAsJson).RootElement; }
        catch { return; }

        var cmd = msg.TryGetProperty("cmd", out var c) ? c.GetString() : null;
        switch (cmd)
        {
            case "ready":
                _readerReady = true;
                await RestoreSessionAsync();
                break;

            case "openDialog":
                ShowOpenDialog();
                break;

            case "drop":
                // Dropped files arrive as CoreWebView2File objects, which carry
                // the real on-disk path (unlike DOM File objects in JS).
                var dropped = new List<string>();
                if (e.AdditionalObjects is { } objects)
                    foreach (var obj in objects)
                        if (obj is CoreWebView2File file &&
                            !string.IsNullOrEmpty(file.Path) && File.Exists(file.Path))
                            dropped.Add(file.Path);
                await OpenFilesAsync(dropped);
                break;

            case "reload":
                if (msg.TryGetProperty("path", out var rp) && rp.GetString() is { } reloadPath)
                {
                    var text = FileWatchService.TryReadFile(reloadPath);
                    if (text is not null)
                        PostToReader(new { type = "changed", path = reloadPath, text });
                }
                break;

            case "closed":
                if (msg.TryGetProperty("path", out var cp) && cp.GetString() is { } closedPath)
                    _watcher.Unwatch(closedPath);
                break;

            case "state":
                UpdateSessionFromState(msg);
                break;

            case "openRelative":
                if (msg.TryGetProperty("base", out var bp) && bp.GetString() is { } basePath &&
                    msg.TryGetProperty("href", out var hp) && hp.GetString() is { } href)
                    await OpenRelativeLinkAsync(basePath, href);
                break;

            case "exportPdf":
                if (msg.TryGetProperty("path", out var ep) && ep.GetString() is { } exportSource)
                    await ExportPdfAsync(exportSource);
                break;

            case "openExternal":
                if (msg.TryGetProperty("url", out var up) && up.GetString() is { } url)
                    OpenExternal(url);
                break;
        }
    }

    /* ---------- Opening files ---------- */

    public async Task OpenFilesAsync(IEnumerable<string> paths)
    {
        if (!_readerReady)
        {
            _pendingFiles.AddRange(paths);
            return;
        }
        foreach (var path in paths)
            await OpenFileAsync(path);
    }

    private Task OpenFileAsync(string path, double scroll = 0)
    {
        var text = FileWatchService.TryReadFile(path);
        if (text is null) return Task.CompletedTask;

        PostToReader(new
        {
            type = "open",
            path,
            name = Path.GetFileName(path),
            text,
            scroll,
        });
        _watcher.Watch(path);
        return Task.CompletedTask;
    }

    private async Task RestoreSessionAsync()
    {
        PostToReader(new { type = "prefs", tocVisible = _session.TocVisible, tocWidth = _session.TocWidth });

        foreach (var f in _session.Files.Where(f => File.Exists(f.Path)))
            await OpenFileAsync(f.Path, f.Scroll);

        if (_session.ActivePath is { } active)
            PostToReader(new { type = "activate", path = active });

        foreach (var f in _startupFiles.Concat(_pendingFiles))
            await OpenFileAsync(f);
        _pendingFiles.Clear();
    }

    private void ShowOpenDialog()
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Markdown (*.md;*.markdown;*.mdown;*.mkd;*.txt)|*.md;*.markdown;*.mdown;*.mkd;*.txt|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) == true)
            _ = OpenFilesAsync(dialog.FileNames);
    }

    private async Task OpenRelativeLinkAsync(string basePath, string href)
    {
        try
        {
            var raw = Uri.UnescapeDataString(href.Split('#')[0]);
            if (raw.Length == 0) return;
            var dir = Path.GetDirectoryName(basePath);
            if (dir is null) return;
            var target = Path.GetFullPath(Path.Combine(dir, raw));
            if (File.Exists(target)) await OpenFileAsync(target);
        }
        catch
        {
            // Malformed link — ignore.
        }
    }

    private async Task ExportPdfAsync(string sourcePath)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export as PDF",
            FileName = Path.GetFileNameWithoutExtension(sourcePath) + ".pdf",
            Filter = "PDF (*.pdf)|*.pdf",
            InitialDirectory = Path.GetDirectoryName(sourcePath),
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            // The @media print stylesheet hides the app chrome, so only the
            // active document is printed.
            bool ok = await webView.CoreWebView2.PrintToPdfAsync(dialog.FileName, null);
            if (!ok)
                MessageBox.Show(this, "The PDF could not be written.", "Export as PDF",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "PDF export failed: " + ex.Message, "Export as PDF",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void OpenExternal(string url)
    {
        if (url.StartsWith("http://") || url.StartsWith("https://") || url.StartsWith("mailto:"))
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { /* no handler registered */ }
        }
    }

    /* ---------- Session persistence ---------- */

    private void UpdateSessionFromState(JsonElement msg)
    {
        var files = new List<SessionFile>();
        if (msg.TryGetProperty("files", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray())
            {
                var p = item.TryGetProperty("path", out var pe) ? pe.GetString() : null;
                if (p is null) continue;
                files.Add(new SessionFile
                {
                    Path = p,
                    Scroll = item.TryGetProperty("scroll", out var se) ? se.GetDouble() : 0,
                });
            }
        }
        _session.Files = files;
        _session.ActivePath = msg.TryGetProperty("activePath", out var ap) ? ap.GetString() : null;
        if (msg.TryGetProperty("tocVisible", out var tv) &&
            tv.ValueKind is JsonValueKind.True or JsonValueKind.False)
            _session.TocVisible = tv.GetBoolean();
        if (msg.TryGetProperty("tocWidth", out var tw) && tw.ValueKind == JsonValueKind.Number)
            _session.TocWidth = Math.Clamp(tw.GetDouble(), 140, 600);
        SessionStore.Save(_session);
    }

    private void RestoreWindowBounds()
    {
        if (_session.WindowLeft is { } left && _session.WindowTop is { } top)
        {
            // Only restore a position that is still on a screen.
            var virtualRight = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth;
            var virtualBottom = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
            if (left < virtualRight - 100 &&
                top < virtualBottom - 100 &&
                left + _session.WindowWidth > SystemParameters.VirtualScreenLeft + 100 &&
                top > SystemParameters.VirtualScreenTop - 10)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = left;
                Top = top;
            }
        }
        Width = _session.WindowWidth;
        Height = _session.WindowHeight;
        if (_session.WindowMaximized) WindowState = WindowState.Maximized;
    }

    private void SaveWindowBounds()
    {
        _session.WindowMaximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            _session.WindowLeft = Left;
            _session.WindowTop = Top;
            _session.WindowWidth = Width;
            _session.WindowHeight = Height;
        }
        else
        {
            _session.WindowLeft = RestoreBounds.Left;
            _session.WindowTop = RestoreBounds.Top;
            _session.WindowWidth = RestoreBounds.Width;
            _session.WindowHeight = RestoreBounds.Height;
        }
        SessionStore.Save(_session);
    }

    /* ---------- Helpers ---------- */

    private void PostToReader(object message)
    {
        if (webView.CoreWebView2 is { } core)
            core.PostWebMessageAsJson(JsonSerializer.Serialize(message));
    }
}

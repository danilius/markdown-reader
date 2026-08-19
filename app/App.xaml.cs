using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Windows;

namespace MarkdownReader;

public partial class App : Application
{
    private const string MutexName = "MarkdownReader_SingleInstance";
    private const string PipeName = "MarkdownReader_Pipe";

    private Mutex? _mutex;
    private CancellationTokenSource? _pipeCts;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var files = e.Args
            .Where(a => !a.StartsWith('-'))
            .Select(a => { try { return Path.GetFullPath(a); } catch { return null; } })
            .Where(p => p is not null && File.Exists(p))
            .Cast<string>()
            .ToList();

        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool isFirst);
        if (!isFirst)
        {
            // Forward the file list to the running instance and quit.
            TryForwardToRunningInstance(files);
            Shutdown();
            return;
        }

        var window = new MainWindow(files);
        MainWindow = window;
        window.Show();

        _pipeCts = new CancellationTokenSource();
        _ = RunPipeServerAsync(window, _pipeCts.Token);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _pipeCts?.Cancel();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private static void TryForwardToRunningInstance(IReadOnlyList<string> files)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000);
            using var writer = new StreamWriter(client, Encoding.UTF8);
            foreach (var f in files) writer.WriteLine(f);
            writer.Flush();
        }
        catch
        {
            // Running instance didn't answer; nothing more we can do.
        }
    }

    private static async Task RunPipeServerAsync(MainWindow window, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(ct);

                using var reader = new StreamReader(server, Encoding.UTF8);
                var files = new List<string>();
                while (await reader.ReadLineAsync(ct) is { } line)
                    if (line.Length > 0) files.Add(line);

                window.Dispatcher.Invoke(() =>
                {
                    if (window.WindowState == WindowState.Minimized)
                        window.WindowState = WindowState.Normal;
                    window.Activate();
                    _ = window.OpenFilesAsync(files);
                });
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // Ignore a bad client and keep serving.
            }
        }
    }
}

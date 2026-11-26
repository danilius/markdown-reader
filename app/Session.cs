using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarkdownReader;

public sealed class SessionFile
{
    public string Path { get; set; } = "";
    public double Scroll { get; set; }
}

public sealed class SessionData
{
    public List<SessionFile> Files { get; set; } = [];
    public string? ActivePath { get; set; }
    public bool TocVisible { get; set; } = true;
    public double TocWidth { get; set; } = 250;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double WindowWidth { get; set; } = 1100;
    public double WindowHeight { get; set; } = 800;
    public bool WindowMaximized { get; set; }
}

public static class SessionStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string Directory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "MarkdownReader");

    private static string FilePath => Path.Combine(Directory, "session.json");

    public static SessionData Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<SessionData>(File.ReadAllText(FilePath), Options)
                       ?? new SessionData();
        }
        catch
        {
            // Corrupt session file — start fresh.
        }
        return new SessionData();
    }

    public static void Save(SessionData data)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(data, Options));
        }
        catch
        {
            // Persistence is best-effort; never crash the app over it.
        }
    }
}

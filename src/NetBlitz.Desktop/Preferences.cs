using System.Text.Json;
using NetBlitz.Core;

namespace NetBlitz.Desktop;

public sealed record Preferences
{
    public string Nickname { get; init; } = Environment.UserName;
    public string Host { get; init; } = "127.0.0.1";
    public int Port { get; init; } = Network.Protocol.DefaultPort;
    public bool HighlightLast { get; init; } = true;
    public bool ShowCheck { get; init; } = true;
    public GameSettings Settings { get; init; } = new();
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetBlitz.Avalonia", "settings.json");

    public static Preferences Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            var preferences = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FilePath));
            return preferences is { Settings.IsValid: true, Port: >= 1 and <= 65535 } ? preferences : new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, FilePath, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace CamPos.Core.Config;

public sealed class OverlayConfig
{
    public bool Visible { get; set; } = true;
    public bool ClickThrough { get; set; }
    public bool AlwaysOnTop { get; set; } = true;
    public int X { get; set; } = 40;
    public int Y { get; set; } = 40;
    public int Width { get; set; } = 360;
    public int Height { get; set; } = 220;
    public double Opacity { get; set; } = 0.98;
    public bool ShowForwardVector { get; set; } = true;
}

public sealed class AppConfig
{
    public int RefreshHz { get; set; } = 30;
    public OverlayConfig Overlay { get; set; } = new();
    public bool ShowDemoOnStartup { get; set; }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string DataDirectory
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CamPos");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string ConfigPath => Path.Combine(DataDirectory, "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
                return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath), Options) ?? new AppConfig();
        }
        catch
        {
        }
        return new AppConfig();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, Options));
        }
        catch
        {
        }
    }
}

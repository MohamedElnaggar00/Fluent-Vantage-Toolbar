using System.Globalization;
using System.Text.Json;
namespace FluentLegionToolbar;

public sealed class AppSettings
{
    public string Language { get; set; } = "en";
    public string Theme { get; set; } = "system";
    public List<string> HiddenTiles { get; set; } = new() { "usb", "refresh" };
    public bool ShowWarranty { get; set; } = true;
    public bool ShowBatteryDetails { get; set; } = true;

    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentLegionToolbar");
    static string FilePath => Path.Combine(Folder, "settings.json");

    public static AppSettings Load()
    {
        try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new(); } catch { }
        return new();
    }

    public void Save()
    {
        try { Directory.CreateDirectory(Folder); File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); } catch { }
    }

    public bool IsTileVisible(string id) => !HiddenTiles.Contains(id);
    public void SetTileVisible(string id, bool visible)
    {
        HiddenTiles.Remove(id);
        if (!visible) HiddenTiles.Add(id);
    }
}

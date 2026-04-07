using System.IO;
using System.Text.Json;
using CropCanvas.Models;

namespace CropCanvas.Services;

public class SettingsService
{
    private static readonly string SettingsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CropCanvas");

    private static readonly string SettingsFile = Path.Combine(SettingsFolder, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public AppSettings Load()
    {
        if (!File.Exists(SettingsFile))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(SettingsFile);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsFolder);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsFile, json);
    }
}

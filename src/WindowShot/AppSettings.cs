using System;
using System.IO;
using System.Text.Json;

namespace WindowShot;

public sealed class AppSettings
{
    public string? SaveFolder { get; set; }
    public bool Topmost { get; set; }
    public bool HotkeyEnabled { get; set; } = true;

    private static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WindowShot",
            "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath))
                       ?? new AppSettings();
            }
        }
        catch
        {
            // Fall back to defaults when settings are unreadable.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var folder = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(folder);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
        }
        catch
        {
            // Settings persistence is optional.
        }
    }

    public string ResolveSaveFolder()
    {
        if (!string.IsNullOrWhiteSpace(SaveFolder))
        {
            return SaveFolder;
        }

        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrWhiteSpace(pictures))
        {
            pictures = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        return Path.Combine(pictures, "WindowShot");
    }
}

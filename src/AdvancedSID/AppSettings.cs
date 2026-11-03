using System.Text.Json;

namespace AdvancedSID;

/// <summary>Remembered between runs in %APPDATA%\AdvancedSID\settings.json.</summary>
internal sealed class AppSettings
{
    public string ProfilePath1 { get; set; } = string.Empty;

    public string OfficePath1 { get; set; } = string.Empty;

    public string ProfilePath2 { get; set; } = string.Empty;

    public string OfficePath2 { get; set; } = string.Empty;

    public bool OfficeEnabled { get; set; }

    public bool CloudCacheEnabled { get; set; }

    public bool DirectoryLookupEnabled { get; set; } = true;

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AdvancedSID", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
            }
        }
        catch
        {
            // Corrupt settings - start fresh.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Not fatal.
        }
    }
}

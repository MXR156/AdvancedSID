using System.Text.Json;
using System.Text.Json.Serialization;
using AdvancedSID.Core;

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

    public bool LockFilesOnly { get; set; }

    /// <summary>True once at least the User Profiles path has been set.</summary>
    [JsonIgnore]
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ProfilePath1);

    /// <summary>Which of the 4 locations are in use: User, Office, User CC, Office CC.</summary>
    public static bool[] ActiveRows(bool office, bool cloudCache) => new[] { true, office, cloudCache, office && cloudCache };

    public List<StorageLocation> BuildLocations()
    {
        var active = ActiveRows(OfficeEnabled, CloudCacheEnabled);
        var all = new[]
        {
            new StorageLocation { Key = "1-P1", Type = ContainerType.Profile, Name = "User Profiles", ShortName = "User Profile", Path = ProfilePath1.Trim() },
            new StorageLocation { Key = "2-O1", Type = ContainerType.Office, Name = "Office Profiles", ShortName = "Office", Path = OfficePath1.Trim() },
            new StorageLocation { Key = "3-P2", Type = ContainerType.Profile, Name = "User Profiles Cloud Cache", ShortName = "User Profile (CC)", Path = ProfilePath2.Trim() },
            new StorageLocation { Key = "4-O2", Type = ContainerType.Office, Name = "Office Profiles Cloud Cache", ShortName = "Office (CC)", Path = OfficePath2.Trim() },
        };
        return all.Where((_, i) => active[i]).ToList();
    }

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

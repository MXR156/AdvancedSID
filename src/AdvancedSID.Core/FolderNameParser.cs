using System.Text.RegularExpressions;

namespace AdvancedSID.Core;

/// <summary>
/// Pulls the SID and user name out of a profile folder name.
/// Handles the FSLogix default "SID_username", the FlipFlop style "username_SID",
/// and UPD style "UVHD-SID" names.
/// </summary>
public static partial class FolderNameParser
{
    [GeneratedRegex(@"S-1-\d+(?:-\d+){1,14}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SidRegex();

    public static (string? Sid, string UserName) Parse(string folderName)
    {
        var match = SidRegex().Match(folderName);
        if (!match.Success)
        {
            return (null, folderName);
        }

        var rest = folderName.Remove(match.Index, match.Length);

        // UPD disks are named UVHD-<SID>.vhdx - strip that prefix if present.
        if (rest.StartsWith("UVHD-", StringComparison.OrdinalIgnoreCase))
        {
            rest = rest[5..];
        }

        rest = rest.Trim('_', ' ', '-', '.');
        return (match.Value.ToUpperInvariant(), rest);
    }

    public static string? FindSid(string text)
    {
        var match = SidRegex().Match(text);
        return match.Success ? match.Value.ToUpperInvariant() : null;
    }
}

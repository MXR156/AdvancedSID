using System.Text;
using System.Text.RegularExpressions;

namespace AdvancedSID.Core;

/// <summary>
/// Reads FSLogix *.lock files. The format is undocumented binary, but the name of the
/// machine that holds the lock is stored as UTF-16 text (it shows up when the file is
/// opened in Notepad), e.g. "    堬ꃰơ  DCLAVDPRDSH-2.dcl.co.uk     ".
/// </summary>
public static partial class LockFileParser
{
    private const int MaxBytesToRead = 64 * 1024;

    // A DNS-ish host name: labels of letters/digits/hyphens separated by dots.
    [GeneratedRegex(@"[A-Za-z0-9](?:[A-Za-z0-9\-]{0,62})(?:\.[A-Za-z0-9](?:[A-Za-z0-9\-]{0,62}))*", RegexOptions.CultureInvariant)]
    private static partial Regex HostRegex();

    public static LockDetails Read(string path)
    {
        byte[] data;
        try
        {
            // The lock may be held open by the session host, so be as permissive as possible.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var length = (int)Math.Min(stream.Length, MaxBytesToRead);
            data = new byte[length];
            var read = 0;
            while (read < length)
            {
                var n = stream.Read(data, read, length - read);
                if (n == 0)
                {
                    break;
                }

                read += n;
            }

            if (read < length)
            {
                Array.Resize(ref data, read);
            }
        }
        catch (Exception ex)
        {
            return new LockDetails { ReadError = ex.Message };
        }

        var candidates = ExtractHostNames(data);
        return new LockDetails
        {
            Server = candidates.Count > 0 ? candidates[0] : null,
            Candidates = candidates,
        };
    }

    /// <summary>Returns host-name-like strings found in the data, best candidate first.</summary>
    public static IReadOnlyList<string> ExtractHostNames(ReadOnlySpan<byte> data)
    {
        var found = new List<string>();

        // FSLogix writes UTF-16LE, but try a plain 8-bit decode too in case that ever changes.
        var utf16 = Encoding.Unicode.GetString(data[..(data.Length & ~1)]);
        Collect(utf16, found);
        Collect(Latin1Printable(data), found);

        return found
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(Score)
            .ThenByDescending(s => s.Length)
            .ToList();
    }

    private static void Collect(string text, List<string> found)
    {
        foreach (Match m in HostRegex().Matches(text))
        {
            var value = m.Value.Trim('-', '.');
            if (IsPlausibleHost(value))
            {
                found.Add(value);
            }
        }
    }

    private static bool IsPlausibleHost(string value)
    {
        if (value.Length < 3 || value.Length > 253)
        {
            return false;
        }

        // Must contain at least one letter; a run of digits is just binary noise.
        if (!value.Any(char.IsAsciiLetter))
        {
            return false;
        }

        // NetBIOS names are max 15 chars; anything longer without a dot is unlikely to be a host.
        var firstLabel = value.Split('.')[0];
        return firstLabel.Length <= 63;
    }

    private static int Score(string value)
    {
        var score = 0;
        if (value.Contains('.'))
        {
            score += 4; // FQDN - by far the most likely
        }

        if (value.Contains('-'))
        {
            score += 1;
        }

        if (value.Length >= 5)
        {
            score += 2;
        }

        return score;
    }

    private static string Latin1Printable(ReadOnlySpan<byte> data)
    {
        var chars = new char[data.Length];
        for (var i = 0; i < data.Length; i++)
        {
            var b = data[i];
            chars[i] = b is >= 0x20 and < 0x7F ? (char)b : ' ';
        }

        return new string(chars);
    }
}

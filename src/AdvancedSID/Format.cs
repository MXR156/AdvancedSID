namespace AdvancedSID;

internal static class Format
{
    public static string Size(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:0.##} {units[unit]}";
    }

    public static string Age(DateTime time)
    {
        if (time == default)
        {
            return string.Empty;
        }

        var span = DateTime.Now - time;
        if (span.TotalMinutes < 1)
        {
            return "just now";
        }

        if (span.TotalHours < 1)
        {
            return $"{(int)span.TotalMinutes}m ago";
        }

        if (span.TotalDays < 1)
        {
            return $"{(int)span.TotalHours}h {span.Minutes}m ago";
        }

        return $"{(int)span.TotalDays}d ago";
    }

    public static string Time(DateTime time) => time == default ? string.Empty : time.ToString("yyyy-MM-dd HH:mm");
}

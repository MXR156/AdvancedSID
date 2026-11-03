using System.Diagnostics;

namespace AdvancedSID;

/// <summary>Helpers for handing things off to Explorer / Notepad / the clipboard.</summary>
internal static class Shell
{
    public static void OpenFolder(string folder) => Run("explorer.exe", $"\"{folder}\"");

    public static void SelectInExplorer(string file) => Run("explorer.exe", $"/select,\"{file}\"");

    public static void OpenInNotepad(string file) => Run("notepad.exe", $"\"{file}\"");

    public static void Copy(string? text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            Clipboard.SetText(text);
        }
    }

    private static void Run(string exe, string args)
    {
        try
        {
            Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "AdvancedSID", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}

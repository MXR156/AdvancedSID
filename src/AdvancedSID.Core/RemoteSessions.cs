using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AdvancedSID.Core;

/// <summary>A session on a remote session host (what "quser /server:HOST" shows).</summary>
public sealed record RemoteSession(int SessionId, string UserName, string Domain, string State);

/// <summary>The sessions on one server, or why they could not be read.</summary>
public sealed class SessionQueryResult
{
    public bool Success => Error is null;

    public string? Error { get; init; }

    public IReadOnlyList<RemoteSession> Sessions { get; init; } = Array.Empty<RemoteSession>();

    public static SessionQueryResult Failed(string error) => new() { Error = error };
}

/// <summary>Lists user sessions on a remote server with the Windows Terminal Services API.</summary>
[SupportedOSPlatform("windows")]
public static class RemoteSessions
{
    private const int WTSUserName = 5;
    private const int WTSDomainName = 7;

    private static readonly string[] StateNames =
    {
        "Active", "Connected", "ConnectQuery", "Shadow", "Disconnected", "Idle", "Listen", "Reset", "Down", "Init",
    };

    public static SessionQueryResult Query(string server)
    {
        var handle = WTSOpenServer(server);
        if (handle == IntPtr.Zero)
        {
            return SessionQueryResult.Failed($"Could not connect ({Marshal.GetLastPInvokeErrorMessage()})");
        }

        try
        {
            if (!WTSEnumerateSessions(handle, 0, 1, out var buffer, out var count))
            {
                return SessionQueryResult.Failed($"Could not list sessions ({Marshal.GetLastPInvokeErrorMessage()})");
            }

            var sessions = new List<RemoteSession>();
            try
            {
                var size = Marshal.SizeOf<WtsSessionInfo>();
                for (var i = 0; i < count; i++)
                {
                    var info = Marshal.PtrToStructure<WtsSessionInfo>(buffer + (i * size));
                    var user = QueryString(handle, info.SessionId, WTSUserName);
                    if (string.IsNullOrEmpty(user))
                    {
                        continue; // services / listener sessions
                    }

                    var state = info.State >= 0 && info.State < StateNames.Length ? StateNames[info.State] : info.State.ToString();
                    sessions.Add(new RemoteSession(info.SessionId, user, QueryString(handle, info.SessionId, WTSDomainName) ?? string.Empty, state));
                }
            }
            finally
            {
                WTSFreeMemory(buffer);
            }

            return new SessionQueryResult { Sessions = sessions };
        }
        finally
        {
            WTSCloseServer(handle);
        }
    }

    private static string? QueryString(IntPtr server, int sessionId, int infoClass)
    {
        if (!WTSQuerySessionInformation(server, sessionId, infoClass, out var buffer, out _))
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WtsSessionInfo
    {
        public int SessionId;
        public IntPtr WinStationName;
        public int State;
    }

    [DllImport("wtsapi32.dll", EntryPoint = "WTSOpenServerW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr WTSOpenServer(string serverName);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSCloseServer(IntPtr server);

    [DllImport("wtsapi32.dll", EntryPoint = "WTSEnumerateSessionsW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WTSEnumerateSessions(IntPtr server, int reserved, int version, out IntPtr sessionInfo, out int count);

    [DllImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WTSQuerySessionInformation(IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out int bytesReturned);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);
}

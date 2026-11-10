using System.Runtime.InteropServices;
namespace SinergiaAI.PrintAgent.Approval;

internal static class DesktopSession
{
    [DllImport("wtsapi32.dll",SetLastError=true)]
    private static extern bool WTSQuerySessionInformation(IntPtr server,int session,int information,out IntPtr buffer,out int bytes);
    [DllImport("wtsapi32.dll")] private static extern void WTSFreeMemory(IntPtr buffer);
    internal static bool IsActive
    {
        get
        {
            // Disconnected Windows profiles must not claim requests from the active desktop.
            if (!WTSQuerySessionInformation(IntPtr.Zero,System.Diagnostics.Process.GetCurrentProcess().SessionId,8,out var buffer,out var bytes)) return false;
            try { return bytes >= 4 && Marshal.ReadInt32(buffer) == 0; }
            finally { WTSFreeMemory(buffer); }
        }
    }
}

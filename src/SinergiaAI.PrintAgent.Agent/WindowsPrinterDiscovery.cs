using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SinergiaAI.PrintAgent.Agent;

/// <summary>Discovers installed queues, including connections owned by active Windows users.</summary>
internal static class WindowsPrinterDiscovery
{
    internal sealed record Printer(string Name, bool IsDefault);

    public static IReadOnlyList<Printer> List()
    {
        var printers = ReadCurrent();
        var defaultName = printers.FirstOrDefault(p => p.IsDefault)?.Name;
        foreach (var token in ActiveUserTokens())
        {
            using (token)
            {
                var userPrinters = WindowsIdentity.RunImpersonated(token, ReadCurrent);
                defaultName ??= userPrinters.FirstOrDefault(p => p.IsDefault)?.Name;
                printers.AddRange(userPrinters);
            }
        }
        return printers.DistinctBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => new Printer(p.Name, string.Equals(p.Name, defaultName, StringComparison.OrdinalIgnoreCase))).ToArray();
    }

    /// <summary>Resolves access before invoking the write exactly once; a failed write is never retried.</summary>
    public static void WithPrinterIdentity(string printerName, Action write)
    {
        bool ContainsPrinter() => ReadCurrent().Any(p => string.Equals(p.Name, printerName, StringComparison.OrdinalIgnoreCase));
        if (ContainsPrinter()) { write(); return; }
        foreach (var token in ActiveUserTokens())
        {
            using (token)
            {
                if (!WindowsIdentity.RunImpersonated(token, ContainsPrinter)) continue;
                WindowsIdentity.RunImpersonated(token, write);
                return;
            }
        }
        throw new InvalidOperationException("The printer is no longer available to the service or an active Windows user.");
    }

    private static List<Printer> ReadCurrent()
    {
        const uint flags = 2 | 4; // PRINTER_ENUM_LOCAL | PRINTER_ENUM_CONNECTIONS
        if (!EnumPrinters(flags, null, 4, IntPtr.Zero, 0, out var size, out _) && Marshal.GetLastWin32Error() != 122)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not enumerate installed printers.");
        if (size == 0) return [];
        var buffer = Marshal.AllocHGlobal(checked((int)size));
        try
        {
            if (!EnumPrinters(flags, null, 4, buffer, size, out _, out var count))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not enumerate installed printers.");
            uint defaultSize = 0;
            GetDefaultPrinter(null, ref defaultSize);
            string? defaultName = null;
            if (defaultSize > 0)
            {
                var name = new StringBuilder(checked((int)defaultSize));
                if (GetDefaultPrinter(name, ref defaultSize)) defaultName = name.ToString();
            }
            var result = new List<Printer>();
            for (var i = 0; i < count; i++)
            {
                var info = Marshal.PtrToStructure<PrinterInfo4>(IntPtr.Add(buffer, checked(i * Marshal.SizeOf<PrinterInfo4>())));
                var name = Marshal.PtrToStringUni(info.PrinterName);
                if (!string.IsNullOrWhiteSpace(name)) result.Add(new(name, string.Equals(name, defaultName, StringComparison.OrdinalIgnoreCase)));
            }
            return result;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static IEnumerable<SafeAccessTokenHandle> ActiveUserTokens()
    {
        using var identity = WindowsIdentity.GetCurrent();
        // WTSQueryUserToken requires LocalSystem. Foreground/custom-account agents use their own queues.
        if (!identity.IsSystem) yield break;
        if (!WTSEnumerateSessions(IntPtr.Zero, 0, 1, out var buffer, out var count))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not enumerate active Windows sessions.");
        try
        {
            for (var i = 0; i < count; i++)
            {
                var session = Marshal.PtrToStructure<SessionInfo>(IntPtr.Add(buffer, i * Marshal.SizeOf<SessionInfo>()));
                if (session.State != 0 || session.SessionId == 0) continue; // WTSActive
                if (WTSQueryUserToken(session.SessionId, out var token)) yield return token;
                else token?.Dispose(); // A user may sign out between enumeration and token acquisition.
            }
        }
        finally { WTSFreeMemory(buffer); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PrinterInfo4 { public IntPtr PrinterName; public IntPtr ServerName; public uint Attributes; }
    [StructLayout(LayoutKind.Sequential)]
    private struct SessionInfo { public uint SessionId; public IntPtr StationName; public int State; }
    [DllImport("winspool.drv", EntryPoint = "EnumPrintersW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumPrinters(uint flags, string? name, uint level, IntPtr buffer, uint size, out uint needed, out uint returned);
    [DllImport("winspool.drv", EntryPoint = "GetDefaultPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetDefaultPrinter(StringBuilder? name, ref uint size);
    [DllImport("wtsapi32.dll", EntryPoint = "WTSEnumerateSessionsW", SetLastError = true)]
    private static extern bool WTSEnumerateSessions(IntPtr server, int reserved, int version, out IntPtr sessions, out int count);
    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out SafeAccessTokenHandle token);
    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);
}

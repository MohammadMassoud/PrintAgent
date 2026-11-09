using System.ComponentModel;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace SinergiaAI.PrintAgent.Agent;

public sealed class WindowsRawReceiptPrinter
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _destinations = new(StringComparer.OrdinalIgnoreCase);

    public async Task PrintAsync(string printerName, byte[] payload, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(printerName))
            throw new InvalidOperationException("A Windows printer name is required.");

        ct.ThrowIfCancellationRequested();
        // Applications have independent queues, but writes to the same destination must not overlap.
        var gate = _destinations.GetOrAdd(printerName, _ => new(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (TryParseTcpEndpoint(printerName, out var host, out var port))
                await PrintTcpAsync(host, port, payload, ct);
            else RawPrinterHelper.SendBytesToPrinter(printerName, payload);
        }
        finally { gate.Release(); }
    }

    private static async Task PrintTcpAsync(string host, int port, byte[] payload, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        ct = timeout.Token;
        using var client = new TcpClient();
        await client.ConnectAsync(host, port, ct);
        await using var stream = client.GetStream();
        await stream.WriteAsync(payload, ct);
        await stream.FlushAsync(ct);
    }

    private static bool TryParseTcpEndpoint(string value, out string host, out int port)
    {
        host = "";
        port = 0;

        var normalized = value.Trim();
        if (normalized.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("socket://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) || uri.Port <= 0)
                return false;

            host = uri.Host;
            port = uri.Port;
            return true;
        }

        var parts = normalized.Split(':', 2);
        if (parts.Length != 2 || !int.TryParse(parts[1], out var parsedPort))
            return false;

        host = parts[0];
        port = parsedPort;
        return !string.IsNullOrWhiteSpace(host);
    }

    private static class RawPrinterHelper
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private sealed class DocInfo
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string DocumentName = "SinergiaAI";
            [MarshalAs(UnmanagedType.LPWStr)] public string? OutputFile;
            [MarshalAs(UnmanagedType.LPWStr)] public string DataType = "RAW";
        }

        [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool OpenPrinter(string printerName, out IntPtr printerHandle, IntPtr printerDefaults);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool ClosePrinter(IntPtr printerHandle);

        [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool StartDocPrinter(IntPtr printerHandle, int level, [In] DocInfo docInfo);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool EndDocPrinter(IntPtr printerHandle);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool StartPagePrinter(IntPtr printerHandle);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool EndPagePrinter(IntPtr printerHandle);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool WritePrinter(IntPtr printerHandle, byte[] data, int count, out int written);

        public static void SendBytesToPrinter(string printerName, byte[] bytes)
        {
            if (!OpenPrinter(printerName, out var printerHandle, IntPtr.Zero))
                ThrowWin32($"Could not open printer '{printerName}'.");

            try
            {
                var docInfo = new DocInfo();
                if (!StartDocPrinter(printerHandle, 1, docInfo))
                    ThrowWin32("Could not start the print document.");

                try
                {
                    if (!StartPagePrinter(printerHandle))
                        ThrowWin32("Could not start the print page.");

                    try
                    {
                        if (!WritePrinter(printerHandle, bytes, bytes.Length, out var written) || written != bytes.Length)
                            ThrowWin32("Could not write all bytes to the printer.");
                    }
                    finally
                    {
                        EndPagePrinter(printerHandle);
                    }
                }
                finally
                {
                    EndDocPrinter(printerHandle);
                }
            }
            finally
            {
                ClosePrinter(printerHandle);
            }
        }

        private static void ThrowWin32(string message)
            => throw new Win32Exception(Marshal.GetLastWin32Error(), message);
    }
}

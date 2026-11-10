using System.IO.Pipes;
using System.Text.Json;
namespace SinergiaAI.PrintAgent.Approval;
internal static class DesktopPipe
{
    public static async Task<string> CallAsync(int port,object request)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var pipe = new NamedPipeClientStream(".","SinergiaAI.PrintAgent.Approval." + port,PipeDirection.InOut,PipeOptions.Asynchronous);
        await pipe.ConnectAsync(timeout.Token);
        await using var writer = new StreamWriter(pipe,leaveOpen:true) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(),timeout.Token);
        using var reader = new StreamReader(pipe,leaveOpen:true);
        return await reader.ReadLineAsync(timeout.Token) ?? throw new IOException("The service closed the connection.");
    }
    internal sealed record Pending(Guid Id,string Origin);
}

using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using SinergiaAI.PrintAgent;

var bridge = new CaptureBridge();
var settings = new PrintAgentOptions();
await using var services = new ServiceCollection().BuildServiceProvider();
await using var agent = new PrintAgent(services, bridge, Options.Create(settings), new Navigation(), NullLogger<PrintAgent>.Instance);
await agent.StartAsync();
var content = "^XA^CI28^FDação\0\u001b^FS^XZ";
var first = await agent.SendAsync("test", content, "text-1");
Require(bridge.LastBytes.SequenceEqual(Encoding.UTF8.GetBytes(content)), "Default UTF-8 preserves accents and control characters without BOM");
Require((await agent.SendByteAsync("test", bridge.LastBytes, "text-1")).Id == first.Id, "Text and equivalent bytes retain the same idempotent request");
settings.TextEncoding = Encoding.ASCII;
var before = bridge.Submissions;
try { await agent.SendAsync("test", "ação", "bad-encoding"); throw new Exception("Expected encoding failure"); }
catch (EncoderFallbackException) { }
Require(bridge.Submissions == before && agent.LastErrorCode == PrintAgentErrorCodes.RequestInvalid, "Unrepresentable characters fail before transport with a safe request error");
await agent.SendAsync("test", "ação", "override", Encoding.UTF8);
Require(bridge.LastBytes.SequenceEqual(Encoding.UTF8.GetBytes("ação")), "Per-send encoding overrides configured default");
Require(ReferenceEquals(settings.TextEncoding.EncoderFallback, Encoding.ASCII.EncoderFallback), "Strict conversion does not mutate the configured encoding");
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
await agent.SendAsync("test", "ação", "code-page", Encoding.GetEncoding(1252));
Require(bridge.LastBytes.SequenceEqual(new byte[] { 0x61, 0xe7, 0xe3, 0x6f }), "Explicit Windows code page produces expected bytes");
var binary = new byte[] { 0, 0x1b, 0x80, 0xff };
await agent.SendByteAsync("test", binary, "binary");
Require(bridge.LastBytes.SequenceEqual(binary), "Binary path never encodes or substitutes bytes");
var prepared = agent.CreateTextDocument("test", "one\r\ntwo", "mixed-text", Encoding.Unicode);
var jobs = await agent.SendBatchAsync([prepared, new PrintDocument("test", binary, "mixed-bytes")]);
Require(jobs.Count == 2 && bridge.Payloads[0].SequenceEqual(Encoding.Unicode.GetBytes("one\r\ntwo")) && bridge.Payloads[1].SequenceEqual(binary), "Mixed batch preserves each document's encoding and binary content");
settings.TextEncoding = Encoding.UTF8;
var automatic1 = await agent.SendAsync("test", "repeat");
var automatic2 = await agent.SendAsync("test", "repeat");
Require(automatic1.Id != automatic2.Id, "Omitting a key creates a new request for every call, including identical content");
var automaticDocument = new PrintDocument("test", "reusable");
var retainedKey = automaticDocument.IdempotencyKey;
var saved1 = await agent.SendAsync(new[] { automaticDocument });
var saved2 = await agent.SendAsync(new[] { automaticDocument });
Require(!string.IsNullOrWhiteSpace(retainedKey) && automaticDocument.IdempotencyKey == retainedKey && saved1[0].Id == saved2[0].Id, "A prepared document retains its generated key when reused");
var restoredDocument = JsonSerializer.Deserialize<PrintDocument>(JsonSerializer.Serialize(automaticDocument))!;
var restoredJobs = await agent.SendAsync(new[] { restoredDocument });
Require(restoredDocument.IdempotencyKey == retainedKey && restoredDocument.Content.SequenceEqual(automaticDocument.Content) && restoredJobs[0].Id == saved1[0].Id, "Persisted documents restore the same bytes and generated identity instead of creating a duplicate");
var textList = new[] { "ação", "line\r\nnext\0" };
var textJobs = await agent.SendAsync("test", textList);
Require(textJobs.Count == 2 && bridge.Payloads[0].SequenceEqual(Encoding.UTF8.GetBytes(textList[0])) && bridge.Payloads[1].SequenceEqual(Encoding.UTF8.GetBytes(textList[1])), "Text-list SendAsync encodes each document in order");
var repeatedTextJobs = await agent.SendAsync("test", textList);
Require(textJobs.Zip(repeatedTextJobs).All(pair => pair.First.Id != pair.Second.Id), "Repeating a plain list creates new requests rather than suppressing intentional copies");
await agent.SendAsync("test", new byte[][] { binary, new byte[] { 0xff, 0x80, 0 } });
Require(bridge.Payloads[0].SequenceEqual(binary) && bridge.Payloads[1].SequenceEqual(new byte[] { 0xff, 0x80, 0 }), "Binary-list SendAsync preserves raw bytes");
await agent.SendAsync(new PrintDocument[] { new("test", "mixed"), new("test", binary) });
Require(bridge.Payloads[0].SequenceEqual(Encoding.UTF8.GetBytes("mixed")) && bridge.Payloads[1].SequenceEqual(binary), "Document constructors accept text or bytes with optional generated keys");
before = bridge.Submissions;
settings.TextEncoding = Encoding.ASCII;
try { await agent.SendAsync("test", new[] { "valid", "ação" }); throw new Exception("Expected list encoding failure"); }
catch (EncoderFallbackException) { }
try { await agent.SendAsync("test", new[] { new string('a', 2 * 1024 * 1024), new string('b', 2 * 1024 * 1024), "c" }); throw new Exception("Expected list size rejection"); }
catch (ArgumentException) { }
try { await agent.SendAsync("test", Array.Empty<string>()); throw new Exception("Expected empty list rejection"); }
catch (ArgumentException) { }
Require(bridge.Submissions == before, "Invalid text lists fail atomically before transport");
before = bridge.Submissions;
try { PrintDocument.FromText("test", "\ud800", "invalid-surrogate", Encoding.UTF8); throw new Exception("Expected invalid Unicode rejection"); }
catch (EncoderFallbackException) { }
try { agent.CreateTextDocument("test", new string('é', 1024 * 1024 + 1), "oversized", Encoding.UTF8); throw new Exception("Expected encoded size rejection"); }
catch (ArgumentException) { }
try { await agent.SendAsync("test", "cancelled", "cancelled", new CancellationToken(true)); throw new Exception("Expected cancellation"); }
catch (OperationCanceledException) { }
Require(before == bridge.Submissions, "Malformed, oversized and cancelled requests never reach the bridge");
Console.WriteLine("All text/binary SDK checks passed. No printer was opened.");

static void Require(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
sealed class Navigation : NavigationManager { public Navigation() => Initialize("https://example.test/", "https://example.test/"); }
sealed class CaptureBridge : IJSRuntime, IJSObjectReference
{
    public int Submissions { get; private set; }
    public byte[][] Payloads { get; private set; } = [];
    public byte[] LastBytes => Payloads[^1];
    private readonly Guid _machine = Guid.NewGuid();
    private readonly Dictionary<string, (string Fingerprint, PrintJob Job)> _jobs = [];
    public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => InvokeAsync<T>(identifier, default, args);
    public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken ct, object?[]? args)
    {
        ct.ThrowIfCancellationRequested();
        if (identifier == "import") return ValueTask.FromResult((T)(object)this);
        if (identifier == "connect") return ValueTask.FromResult(JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(new { Protocol = 2, Version = "2.0.0", MachineId = _machine, MachineName = "Capture", Printers = Array.Empty<PrintPrinter>() }))!);
        if (identifier == "request")
        {
            RequireOperation((string)args![0]! == "enqueue");
            var documents = JsonSerializer.SerializeToElement(args[1]).GetProperty("documents").EnumerateArray().ToArray();
            Submissions++;
            Payloads = documents.Select(d => Convert.FromBase64String(d.GetProperty("contentBase64").GetString()!)).ToArray();
            var jobs = documents.Select(d =>
            {
                var key = d.GetProperty("IdempotencyKey").GetString()!;
                var fingerprint = d.GetProperty("PrinterId").GetString() + ":" + d.GetProperty("contentBase64").GetString();
                if (_jobs.TryGetValue(key, out var old)) { RequireOperation(old.Fingerprint == fingerprint); return old.Job; }
                var job = new PrintJob(Guid.NewGuid(), _machine, "test", PrintJobStatus.Pending, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
                _jobs.Add(key, (fingerprint, job)); return job;
            }).ToArray();
            return ValueTask.FromResult((T)(object)jobs);
        }
        return ValueTask.FromResult(default(T)!);
    }
    private static void RequireOperation(bool condition) { if (!condition) throw new InvalidOperationException("Unexpected bridge request"); }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

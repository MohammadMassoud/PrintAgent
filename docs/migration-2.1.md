# Unified sending in 2.1

Use one method name for text, binary content or a list:

```csharp
await PrintAgent.SendAsync(printerId, commands);         // string
await PrintAgent.SendAsync(printerId, rawBytes);         // byte[]
await PrintAgent.SendAsync(printerId, commandList);      // IReadOnlyList<string>
await PrintAgent.SendAsync(printerId, binaryList);       // IReadOnlyList<byte[]>
await PrintAgent.SendAsync(documents);                  // IReadOnlyList<PrintDocument>
```

The overload is chosen by C# from the argument type. No object-based guessing, document rendering or printer-language detection is performed. A list of strings is one job per element, encoded with `PrintAgentOptions.TextEncoding`; a byte array is one binary document. The same count and byte-size limits apply to every overload.

The caller's request key is optional for single sends and document constructors:

```csharp
await PrintAgent.SendAsync(printerId, commands); // New request on every call.
await PrintAgent.SendAsync(printerId, commands, idempotencyKey: savedKey);

var documents = new[]
{
    new PrintDocument(printerId, commands), // Strict UTF-8; generates a key once.
    new PrintDocument(printerId, rawBytes, savedBinaryKey),
    PrintAgent.CreateTextDocument(otherPrinterId, otherCommands, encoding: Encoding.ASCII)
};
// Persist each document's PrinterId, Content and IdempotencyKey before submitting
// if the request must be recovered after a lost response or application restart.
await PrintAgent.SendAsync(documents);
```

Each `PrintDocument` retains its generated key. Submitting the same document again uses that same identity. In contrast, calling a plain text/byte/list overload again without explicit keys creates new jobs, even when the content is identical. A list of strings or byte arrays generates an independent key for each element on every call; use prepared documents when identities must survive retries.

An explicit empty or whitespace key is invalid. Only omission or null requests an automatically generated key. Automatic keys are random, not content hashes: intentionally printing the same content twice remains possible. Do not automatically call again after a lost response without saved keys; the earlier submission may already have reached the printer.

Text defaults to strict UTF-8 without a BOM; configure `TextEncoding` or use the existing explicit-encoding overload: `SendAsync(printerId, commands, null, Encoding.ASCII)`. For per-document encoding, use `CreateTextDocument`. No printer commands or line endings are added.

`SendByteAsync`, `SendBatchAsync` and all 2.0 signatures remain binary-compatible forwarding aliases. They are hidden from IntelliSense so `SendAsync` is the main sending entry point. Existing applications do not need to change stored keys, payloads, printer IDs or queue data. The Windows agent remains 2.0.0 on protocol 2; this SDK-only improvement does not require a new installer.

The local durable queue, status history and no-automatic-replay rule remain enabled even when the application omits keys. Idempotency is duplicate-request protection, not confirmation of physical paper output. Cancelling a wait still cannot retract an already accepted job.

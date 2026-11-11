# Upgrade to 2.0

Version 2.0 names text and binary printing explicitly. Update existing byte-array calls:

```csharp
// Before 2.0:
// await PrintAgent.SendAsync(printerId, rawBytes, requestKey);

// In 2.0:
await PrintAgent.SendByteAsync(printerId, rawBytes, requestKey);
```

`SendAsync` now accepts text commands. The default encoding is strict UTF-8 without a BOM:

```csharp
await PrintAgent.SendAsync(printerId, zpl, requestKey);

// Override the encoding for a specific printer:
await PrintAgent.SendAsync(printerId, asciiCommands, requestKey, Encoding.ASCII);
```

Do not generate a new request key during migration for work that may already have been accepted. Keep the original destination and exact bytes; `SendByteAsync` uses the same protocol-2 enqueue operation and idempotency scope. `SendBatchAsync` and the binary `PrintDocument` constructor are unchanged.

Configure the default once through `AddPrintAgent(o => o.TextEncoding = Encoding.ASCII)`. UTF-8 is not supported by every printer. For legacy code pages, register `CodePagesEncodingProvider.Instance` in the server application before selecting `Encoding.GetEncoding(1252)` or the required printer code page. Conversion always rejects unrepresentable characters instead of silently replacing them.

For durable text requests, save the original text and chosen encoding, or prepare and persist the exact bytes:

```csharp
var document = PrintAgent.CreateTextDocument(printerId, commands, requestKey);
// Persist document.PrinterId, document.Content and document.IdempotencyKey here.
await PrintAgent.SendByteAsync(document.PrinterId, document.Content, document.IdempotencyKey);
```

Prepared text documents can be mixed with binary documents in `SendBatchAsync`. The limits apply to encoded bytes, not string length: 2 MiB per document, 4 MiB per batch and at most 5,000 documents.

The Windows agent remains on protocol 2. Install the 2.0 agent to include shared printer discovery from active user sessions. An approved website can see machine queues and the shared queues of active users; custom-account agents see their own account's queues. Update in place and retain ProgramData, the local queue, WAL files and journals. The update does not import, replay or automatically resubmit old jobs.

Cancelling a `CancellationToken` stops waiting where possible; it does not retract accepted work. `Sent` means the bytes reached the spooler or TCP stream, not that paper output was verified. A job with uncertain delivery is never automatically sent again.

The NuGet contains English XML documentation for the current public facade, options, models and errors. Visual Studio uses it for IntelliSense and metadata navigation. Whether F12 shows metadata signatures or decompiled implementation remains an IDE setting.

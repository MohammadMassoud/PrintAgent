using System.Text;
using System.ComponentModel;

namespace SinergiaAI.PrintAgent;

public sealed partial class PrintAgent
{
    /// <summary>Encodes text commands with the configured TextEncoding and queues them for RAW printing.</summary>
    /// <param name="printerId">The destination identifier from PrintersInfo.</param>
    /// <param name="content">Text commands in a language and encoding understood by the printer.</param>
    /// <param name="idempotencyKey">An optional stable key. Omission creates a new request on every call, even when content is identical.</param>
    /// <param name="ct">Cancels submission waiting. It cannot retract an already accepted print job.</param>
    /// <returns>The accepted job, or the original job for an identical request key.</returns>
    /// <exception cref="EncoderFallbackException">The configured encoding cannot represent the text.</exception>
    /// <remarks>No BOM, line endings, layout or escape substitutions are added. Without an explicit stable key, calling again creates another job. Prepare and save a PrintDocument before sending when submission must be recoverable.</remarks>
    public Task<PrintJob> SendAsync(string printerId, string content, string? idempotencyKey = null, CancellationToken ct = default)
        => SendAsync(printerId, content, idempotencyKey, options.Value.TextEncoding, ct);

    /// <summary>Encodes text commands with an explicit encoding and queues them for RAW printing.</summary>
    /// <param name="printerId">The destination identifier from PrintersInfo.</param>
    /// <param name="content">Printer-compatible text commands. This method does not render a text document.</param>
    /// <param name="idempotencyKey">An optional stable key. Pass null to create a new request identity.</param>
    /// <param name="encoding">The printer's expected encoding. Unrepresentable characters are rejected.</param>
    /// <param name="ct">Cancels submission waiting; accepted jobs continue independently.</param>
    /// <returns>The accepted or previously accepted job.</returns>
    /// <exception cref="EncoderFallbackException">The encoding cannot represent the text.</exception>
    public Task<PrintJob> SendAsync(string printerId, string content, string? idempotencyKey, Encoding encoding, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(encoding);
        var document = CreateTextDocument(printerId, content, idempotencyKey, encoding);
        return SendAsync(document.PrinterId, document.Content, document.IdempotencyKey, ct);
    }

    /// <summary>Queues exact binary content without encoding, rendering or conversion.</summary>
    /// <param name="printerId">The destination identifier from PrintersInfo.</param>
    /// <param name="content">Printer-ready bytes, between 1 byte and 2 MiB. Do not mutate during submission.</param>
    /// <param name="idempotencyKey">An optional stable key. Omission creates a new request on every call; prepare a PrintDocument to retain a generated key.</param>
    /// <param name="ct">Cancels submission waiting. A lost response does not prove that the job was rejected.</param>
    /// <returns>The accepted job, or the original job for an identical request key.</returns>
    /// <remarks>Sent confirms delivery to the spooler or TCP stream, not physical output. Never create a new key to retry an uncertain delivery automatically.</remarks>
    public async Task<PrintJob> SendAsync(string printerId, byte[] content, string? idempotencyKey = null, CancellationToken ct = default)
        => (await SendAsync(new PrintDocument[] { new(printerId, content, idempotencyKey) }, ct))[0];

    /// <summary>Prepares a text document for persistence or a mixed text/binary batch without contacting the agent.</summary>
    /// <param name="printerId">The selected destination identifier.</param>
    /// <param name="content">Printer-compatible text commands.</param>
    /// <param name="idempotencyKey">An optional stable key. Omission generates one key for the returned document, which can be persisted before sending.</param>
    /// <param name="encoding">An optional encoding override. The configured TextEncoding is used otherwise.</param>
    /// <returns>The exact encoded document. Persist Content, PrinterId and IdempotencyKey before submission.</returns>
    /// <exception cref="ArgumentException">The content, destination, key or encoded size is invalid.</exception>
    /// <exception cref="EncoderFallbackException">The selected encoding cannot represent the text.</exception>
    public PrintDocument CreateTextDocument(string printerId, string content, string? idempotencyKey = null, Encoding? encoding = null)
    {
        try { return PrintDocument.FromText(printerId, content, idempotencyKey, encoding ?? options.Value.TextEncoding); }
        catch (ArgumentException) { RecordError(PrintAgentErrorCodes.RequestInvalid); throw; }
    }

    /// <summary>Encodes and atomically queues an ordered list of text commands for one printer.</summary>
    /// <param name="printerId">The destination identifier from PrintersInfo.</param>
    /// <param name="contents">One text command document per element, using the configured TextEncoding.</param>
    /// <param name="ct">Cancels submission waiting; accepted jobs may continue.</param>
    /// <returns>One job per element in input order.</returns>
    /// <remarks>Each element receives a new key for this call. Repeating the call creates new jobs. Use prepared PrintDocument instances for stable per-document keys or encoding overrides.</remarks>
    public Task<IReadOnlyList<PrintJob>> SendAsync(string printerId, IReadOnlyList<string> contents, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ValidateList(contents);
        var documents = new List<PrintDocument>(contents.Count);
        long total = 0;
        foreach (var content in contents)
        {
            var document = CreateTextDocument(printerId, content);
            total += document.Content.Length;
            if (total > 4 * 1024 * 1024)
            {
                RecordError(PrintAgentErrorCodes.RequestInvalid);
                throw new ArgumentException("A batch may contain at most 4 MiB of encoded bytes.", nameof(contents));
            }
            documents.Add(document);
        }
        return SendAsync(documents, ct);
    }

    /// <summary>Atomically queues an ordered list of exact binary documents for one printer.</summary>
    /// <param name="printerId">The destination identifier from PrintersInfo.</param>
    /// <param name="contents">One printer-ready byte array per document.</param>
    /// <param name="ct">Cancels submission waiting; accepted jobs may continue.</param>
    /// <returns>One job per element in input order.</returns>
    /// <remarks>Each element receives a new key for this call. Use prepared PrintDocument instances to preserve keys across calls.</remarks>
    public Task<IReadOnlyList<PrintJob>> SendAsync(string printerId, IReadOnlyList<byte[]> contents, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ValidateList(contents);
        return SendAsync(contents.Select(content => new PrintDocument(printerId, content)).ToArray(), ct);
    }

    private void ValidateList<T>(IReadOnlyList<T> contents)
    {
        if (contents is not null && contents.Count is >= 1 and <= 5000) return;
        RecordError(PrintAgentErrorCodes.RequestInvalid);
        throw new ArgumentException("Submit between 1 and 5,000 documents.", nameof(contents));
    }

    /// <summary>Compatibility alias for the binary SendAsync overload.</summary>
    /// <param name="printerId">The destination identifier.</param>
    /// <param name="content">Exact printer-ready bytes.</param>
    /// <param name="idempotencyKey">An optional stable request key.</param>
    /// <param name="ct">Cancels submission waiting.</param>
    /// <returns>The accepted or previously accepted job.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public Task<PrintJob> SendByteAsync(string printerId, byte[] content, string? idempotencyKey = null, CancellationToken ct = default)
        => SendAsync(printerId, content, idempotencyKey, ct);

    /// <summary>Compatibility alias for the document-list SendAsync overload.</summary>
    /// <param name="documents">Prepared documents with explicit or generated request keys.</param>
    /// <param name="ct">Cancels submission waiting.</param>
    /// <returns>Accepted jobs in document order.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public Task<IReadOnlyList<PrintJob>> SendBatchAsync(IReadOnlyList<PrintDocument> documents, CancellationToken ct = default)
        => SendAsync(documents, ct);
}

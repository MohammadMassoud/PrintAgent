using System.Text;

namespace SinergiaAI.PrintAgent;

public sealed partial class PrintAgent
{
    /// <summary>Encodes text commands with the configured TextEncoding and queues them for RAW printing.</summary>
    /// <param name="printerId">The destination identifier from PrintersInfo.</param>
    /// <param name="content">Text commands in a language and encoding understood by the printer.</param>
    /// <param name="idempotencyKey">A stable, persisted request key. Reuse it only with the same destination and encoded bytes.</param>
    /// <param name="ct">Cancels submission waiting. It cannot retract an already accepted print job.</param>
    /// <returns>The accepted job, or the original job for an identical request key.</returns>
    /// <exception cref="EncoderFallbackException">The configured encoding cannot represent the text.</exception>
    /// <remarks>No BOM, line endings, layout or escape substitutions are added. Persist the text and encoding, or the exact encoded bytes, before submission.</remarks>
    public Task<PrintJob> SendAsync(string printerId, string content, string idempotencyKey, CancellationToken ct = default)
        => SendAsync(printerId, content, idempotencyKey, options.Value.TextEncoding, ct);

    /// <summary>Encodes text commands with an explicit encoding and queues them for RAW printing.</summary>
    /// <param name="printerId">The destination identifier from PrintersInfo.</param>
    /// <param name="content">Printer-compatible text commands. This method does not render a text document.</param>
    /// <param name="idempotencyKey">The stable request key saved before the first attempt.</param>
    /// <param name="encoding">The printer's expected encoding. Unrepresentable characters are rejected.</param>
    /// <param name="ct">Cancels submission waiting; accepted jobs continue independently.</param>
    /// <returns>The accepted or previously accepted job.</returns>
    /// <exception cref="EncoderFallbackException">The encoding cannot represent the text.</exception>
    public Task<PrintJob> SendAsync(string printerId, string content, string idempotencyKey, Encoding encoding, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(encoding);
        var document = CreateTextDocument(printerId, content, idempotencyKey, encoding);
        return SendByteAsync(document.PrinterId, document.Content, document.IdempotencyKey, ct);
    }

    /// <summary>Queues exact binary content without encoding, rendering or conversion.</summary>
    /// <param name="printerId">The destination identifier from PrintersInfo.</param>
    /// <param name="content">Printer-ready bytes, between 1 byte and 2 MiB. Do not mutate during submission.</param>
    /// <param name="idempotencyKey">The stable request key persisted with this destination and byte sequence.</param>
    /// <param name="ct">Cancels submission waiting. A lost response does not prove that the job was rejected.</param>
    /// <returns>The accepted job, or the original job for an identical request key.</returns>
    /// <remarks>Sent confirms delivery to the spooler or TCP stream, not physical output. Never create a new key to retry an uncertain delivery automatically.</remarks>
    public async Task<PrintJob> SendByteAsync(string printerId, byte[] content, string idempotencyKey, CancellationToken ct = default)
        => (await SendBatchAsync([new PrintDocument(printerId, content, idempotencyKey)], ct))[0];

    /// <summary>Prepares a text document for persistence or a mixed text/binary batch without contacting the agent.</summary>
    /// <param name="printerId">The selected destination identifier.</param>
    /// <param name="content">Printer-compatible text commands.</param>
    /// <param name="idempotencyKey">The stable request key to save with the returned content.</param>
    /// <param name="encoding">An optional encoding override. The configured TextEncoding is used otherwise.</param>
    /// <returns>The exact encoded document. Persist Content, PrinterId and IdempotencyKey before submission.</returns>
    /// <exception cref="ArgumentException">The content, destination, key or encoded size is invalid.</exception>
    /// <exception cref="EncoderFallbackException">The selected encoding cannot represent the text.</exception>
    public PrintDocument CreateTextDocument(string printerId, string content, string idempotencyKey, Encoding? encoding = null)
    {
        try { return PrintDocument.FromText(printerId, content, idempotencyKey, encoding ?? options.Value.TextEncoding); }
        catch (ArgumentException) { RecordError(PrintAgentErrorCodes.RequestInvalid); throw; }
    }
}

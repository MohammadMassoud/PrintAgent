# SinergiaPrint.Contracts

Shared contracts for SinergiaPrint, a raw-printing bridge between .NET applications and printers reachable by a Windows agent.

Targets .NET 10. This is an initial preview.

Includes printer and installation information, raw print-job requests, claim/acknowledgment messages, job status, and enrollment configuration contracts.

- Use **SinergiaPrint.Server** to embed the API in your application's backend.
- Use **SinergiaPrint.Client** to communicate with a separately hosted SinergiaPrint API.
- The Windows agent is installed separately on the computer that can reach the printers.

`Sent` means data was sent to the Windows spooler or TCP stream. It does not confirm physical printing. `Unknown` requires checking the printer before deliberately submitting a new job.

The agent forwards supplied bytes without detecting the printer language or converting documents. Your application generates ZPL, ESC/POS, or another language supported by the selected printer.

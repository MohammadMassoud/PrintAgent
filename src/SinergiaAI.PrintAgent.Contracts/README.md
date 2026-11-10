> Legacy remote-API types retained for controlled maintenance. New integrations should use the direct WebSocket facade documented in the [main README](../../README.md).

# SinergiaAI.PrintAgent.Contracts

Internal component included in the single **SinergiaAI.PrintAgent** NuGet package. This project no longer produces a separate package. See [the unified package README](../SinergiaAI.PrintAgent.Server/README.md).

Shared contracts for PrintAgent, a RAW printing bridge between .NET applications and printers reachable by a Windows agent.

Targets .NET 10.

Includes printer and installation information, raw print-job requests, claim/acknowledgment messages, job status, and enrollment configuration contracts.

- Use **SinergiaAI.PrintAgent.Server** to embed the API in your application's backend.
- Use **SinergiaAI.PrintAgent.Client** to communicate with a separately hosted SinergiaPrint API.
- The Windows agent is installed separately on the computer that can reach the printers.

`Sent` means data was sent to the Windows spooler or TCP stream. It does not confirm physical printing. `Unknown` requires checking the printer before deliberately submitting a new job.

The agent forwards supplied bytes without detecting the printer language or converting documents. Your application generates ZPL, ESC/POS, or another language supported by the selected printer.

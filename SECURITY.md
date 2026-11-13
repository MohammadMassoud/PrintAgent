# Security policy

## Supported release line

Security fixes are developed for the current stable integration: SDK 2.1.x with Windows agent 2.0.x, protocol 2. Users on earlier releases should review the migration documentation. This is a maintenance policy, not a service-level or response-time guarantee.

## Report a vulnerability privately

Use [GitHub private vulnerability reporting](https://github.com/MohammadMassoud/PrintAgent/security/advisories/new). Do not disclose exploitable details, tokens or sensitive print data in a public issue.

Include the affected SDK and agent versions, Windows version, impact, prerequisites and a minimal reproduction. Use synthetic print payloads. If the reporting form is unavailable, open a public issue containing only a request for a private contact channel, without vulnerability details.

## Security boundaries

- Only authorize trusted website origins. Authorization is shared across the Windows workstation and covers the full origin, not an individual page or application login.
- Local native processes are part of the workstation trust boundary.
- RAW commands can affect printer behavior. The application must choose a compatible printer and supply appropriate bytes; PrintAgent does not validate printer languages or render documents.
- Backend credentials belong on the server. Do not embed them in WebAssembly or public browser scripts.
- `Sent` confirms delivery to the Windows spooler or TCP endpoint. An uncertain result must not be automatically replayed.

See [integration](docs/integration.md), [troubleshooting](docs/troubleshooting.md) and [download verification](docs/downloads.md) for deployment and recovery details.

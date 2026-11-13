# Changelog

Stable NuGet packages are distributed through [NuGet.org](https://www.nuget.org/packages/SinergiaAI.PrintAgent). GitHub Releases collect notes, compatible Windows downloads and checksums. SDK and agent versions may differ when a release changes only the integration package.

## 2.1.0 — Unified sending API

**SDK:** 2.1.0. **Compatible Windows agent:** 2.0.0, protocol 2.

- Use `SendAsync` for text, binary data, text lists, byte-array lists and prepared `PrintDocument` lists.
- Caller-supplied request keys are optional. Omitting a key creates a new request on each plain send call, even for identical content.
- Prepared documents generate their identity once and retain it when reused. Persist their identities, destinations and payloads before submission when recovery is required.
- Existing `SendByteAsync`, `SendBatchAsync` and 2.0 signatures remain compatible aliases, hidden from IntelliSense.
- Strict text encoding, exact binary transport, durable local queue and protection against automatic replay of uncertain deliveries are preserved.
- This is an SDK-only release: it does not rebuild or replace the Windows agent installer.

[Release and downloads](https://github.com/MohammadMassoud/PrintAgent/releases/tag/v2.1.0) · [Migration and examples](docs/migration-2.1.md)

## 2.0.0 — Text and binary RAW printing

- Added typed sending APIs and strict text encoding with UTF-8 as the default.
- Binary payloads remain unchanged; no BOM, line endings or printer-language conversion is added.
- Added English XML API documentation for IntelliSense.
- Continued support for compatible label, receipt and ordinary-paper printers through Windows queues and configured TCP destinations.

[Migration guide](docs/migration-2.0.md)

## 1.1.0 — Windows desktop controls

- Added the Windows tray application, Start menu shortcut and service start/stop controls.
- Added native website authorization requested by `StartAsync` and management of approved websites.
- Localized desktop controls in English and Portuguese.

## 1.0.0 — Direct local WebSocket printing

- Introduced browser-to-agent WebSocket transport with a persistent local queue and website-origin authorization.
- Removed the need for a remote printing API or application-server printing database in the default integration.
- Preserved legacy journals and queues without automatic migration or replay.

[Migration from 0.x](docs/migration-1.0.md)

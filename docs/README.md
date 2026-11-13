# PrintAgent documentation — Blazor and .NET RAW printing

PrintAgent by SinergiaAI connects Blazor Server / Interactive Server applications to Windows and configured TCP printers. Applications generate printer-ready bytes; the agent forwards them without rendering documents or detecting the printer language.

## Start here

- [English quick start](../README.md) and [guia em português](../README.pt-BR.md).
- [Stable downloads, versions and SHA-256 verification](downloads.md).
- [Full integration reference](integration.md).
- [Executable Blazor demo with a simulated TCP printer](../samples/PrintAgent.Demo/README.md).

## Printing examples

| Task | English | Português |
| --- | --- | --- |
| Print ZPL labels from Blazor Server | [ZPL label guide](https://www.sinergiaai.com/printagent/blazor-zpl-label-printing.html) | [Etiquetas ZPL no Blazor](https://www.sinergiaai.com/printagent/imprimir-etiquetas-blazor.html) |
| Send ESC/POS receipt commands | [ESC/POS receipt guide](https://www.sinergiaai.com/printagent/blazor-escpos-receipt-printing.html) | [Impressão ESC/POS com C#](https://www.sinergiaai.com/printagent/impressao-escpos-blazor.html) |
| Send text, bytes or lists with SDK 2.1 | [SendAsync examples](migration-2.1.md) | [Exemplos no README em português](../README.pt-BR.md#uma-api-para-texto-bytes-e-listas-na-versão-21) |

ZPL, ESC/POS, PCL and PostScript support depends on the target printer. PrintAgent does not convert HTML, PDF, Office documents or images, and there is no standalone WebAssembly SDK in this release.

## Diagnose and upgrade

- [Connection, authorization and delivery errors](troubleshooting.md).
- [SDK 2.1 compatibility and optional request keys](migration-2.1.md).
- [2.0 encoding and binary API migration](migration-2.0.md).
- [Migration from 0.x](migration-1.0.md).
- [Changelog](../CHANGELOG.md).

After a lost response or an uncertain delivery, inspect the job and printer before authorizing another copy. Never automatically retry a keyless call. `Sent` confirms byte delivery to a Windows queue or TCP destination, not paper output.

## Help and contribute

- [Report a reproducible issue](https://github.com/MohammadMassoud/PrintAgent/issues/new/choose).
- [Contribution and local validation guide](../CONTRIBUTING.md).
- [Private vulnerability reporting](../SECURITY.md).
- [Maintainer release checklist](nuget-publication.md).

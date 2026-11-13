# Contributing to PrintAgent

Bug reports, reproducible examples and focused improvements are welcome in English or Portuguese. Start with the [documentation](docs/README.md) and [open issues](https://github.com/MohammadMassoud/PrintAgent/issues).

## Report a problem

Use the bug report template and include the SDK version, agent version, Windows version, Blazor hosting mode, exact error code and steps to reproduce. Reproduce transport issues with the [simulated demo](samples/PrintAgent.Demo/README.md) when possible. Describe the printer model and accepted language only when relevant.

Remove API keys, tokens, website credentials, customer data and actual sensitive print payloads from reports. Report vulnerabilities through [the private security channel](SECURITY.md).

## Develop on Windows

Use Windows x64, the .NET 10 SDK and Python 3. Inno Setup is needed only when building a new installer; it is not required for the normal build and transport tests.

```shell
dotnet build SinergiaAI.PrintAgent.slnx
python tests/integration.py
python tests/websocket_integration.py
dotnet run --project tests/ErrorHandling --no-build
dotnet run --project tests/Sending --no-build
```

These checks use temporary processes, test bridges and loopback TCP receivers. Do not install a permanent service or send to a physical printer in automated tests. Additional installer, package and browser checks are described in [the release guide](docs/nuget-publication.md).

## Keep the transport contract

- Keep PrintAgent independent from application-specific business rules.
- Forward binary payloads unchanged. Text encoding must be explicit and strict; do not silently convert documents or detect printer languages.
- Preserve local journals, request identities and delivery-state history.
- Never automatically resend work that may already have reached a printer. A cancelled wait cannot retract accepted work.
- Keep server SDK credentials and backend keys out of WebAssembly and public browser assets.
- Keep website-origin approval explicit and local to Windows.

## Submit a pull request

Explain the concrete problem, the resulting behavior and the checks you ran. Add meaningful coverage when changing transport, persistence, authorization or recovery behavior. Keep documentation examples aligned with the published SDK and compatible agent versions. Do not commit generated installers, NuGet packages, databases, credentials or local configuration; release downloads are attached as GitHub Release assets.

By contributing, you agree that your contributions are distributed under this repository's [MIT license](LICENSE).

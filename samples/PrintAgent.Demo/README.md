# PrintAgent demo

Standalone Blazor Interactive Server example using direct WebSocket printing. Requires Windows x64 and .NET 10 SDK.

```shell
git clone https://github.com/MohammadMassoud/PrintAgent.git
cd PrintAgent
dotnet run --project samples/PrintAgent.Demo -- --simulate
```

Open http://localhost:5199. Select **Simulated TCP printer**, choose ZPL or ESC/POS and queue a request. The temporary console agent owns an isolated queue and sends exact bytes to a loopback TCP receiver. No Windows service is installed; physical printers are excluded. Its own origin is preapproved through temporary configuration. Normal installations use the Windows authorization dialog.

The example shows pushed job updates, persistent request keys, retries without duplicate delivery and actionable errors. There is no application-server printing database or API polling. Its loopback-only sample login is not a production authentication design.

Omit `--simulate` to use an installed agent. Authorize the website through the Windows dialog and refresh before choosing a printer. That mode can print physically.

Error examples, one at a time:

```shell
dotnet run --project samples/PrintAgent.Demo -- --simulate --Demo:ErrorExample=authorization
dotnet run --project samples/PrintAgent.Demo -- --simulate --Demo:ErrorExample=unavailable
```

These demonstrate unapproved origins and absent agents without changing system permissions or deleting dependencies. See [migration](../../docs/migration-1.0.md) before using an old installation.

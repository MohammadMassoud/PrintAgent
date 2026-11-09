# PrintAgent demo

A standalone Blazor Server application for machine discovery, printer selection, RAW submission and queue results. It references source projects in this repository, so cloning and running does not depend on a published NuGet version.

## Simulated printer

On Windows x64 with the .NET 10 SDK:

```shell
git clone https://github.com/MohammadMassoud/PrintAgent.git
cd PrintAgent
dotnet run --project samples/PrintAgent.Demo -- --simulate
```

Open **http://localhost:5199**. Wait for **Agent connected**, select **Simulated TCP printer**, choose ZPL or ESC/POS, and submit a sample. The page shows jobs and captured bytes. Submit several copies to exercise the queue.

This mode starts a temporary console agent and TCP listener. It does not install a service or print physically. Ctrl+C stops the host and its child agent. Captures are kept in memory; queue data and keys are stored under `.demo-data/` in the project directory, which Git ignores.

The demo retains each request's destination, payload and idempotency keys in browser storage before submission. Retrying or reloading uses the same keys. Use **New request** when you deliberately want another print operation.

## Real printer

```shell
dotnet run --project samples/PrintAgent.Demo
```

Open the same address. If no compatible service is recognized, click **Download installer**, run the downloaded EXE with administrator approval, then refresh the connection. A browser may ask for local-network access permission.

Select a printer that accepts the chosen RAW format. ZPL requires a ZPL-capable printer; ESC/POS requires an ESC/POS-capable printer. The agent does not validate the language. Submitted jobs may produce physical output.

Windows agent 0.5.0 can serve this demo alongside other integrated applications without replacing their connections. Older agents require an upgrade to support multiple applications. Keep the demo running while testing: its API exists only while the process is active. Simulation remains available without changing the installed service.

## Configuration

| Setting | Default | Purpose |
| --- | --- | --- |
| Application URL | `http://localhost:5199` | Browser and API address |
| `Demo:DataDirectory` | `.demo-data` | Queue, keys and simulation data |
| `Demo:BridgePort` | `17831`, or `17832` in simulation | Local discovery bridge |

```shell
dotnet run --project samples/PrintAgent.Demo -- --simulate --urls http://localhost:5299 --Demo:BridgePort 17833
```

Simulation uses a separate bridge port to coexist with an installed service. If a port is occupied, stop the earlier demo or choose another. Use the same URL consistently: `localhost` and `127.0.0.1` are different browser origins.

The application accepts only loopback clients. Its fixed demonstration login is for local testing; production must provide authentication and authorization, HTTPS, durable storage and persistent Data Protection keys.

## Project layout

- `Program.cs`: host, demonstration authentication and integration registration.
- `Components/Pages/Home.razor` and `Home.razor.cs`: selection, requests and results.
- `wwwroot/demo.css`: responsive page styles.
- `DemoSimulator.cs`: temporary agent process and simulated destination.

The page uses `@inject PrintAgent PrintAgent` and the `SinergiaAI.PrintAgent` namespace. Simulation infrastructure uses advanced backend APIs to provision a temporary installation.

## Browser verification

Optional, with Node.js and Playwright:

```shell
npm install --prefix artifacts/browser-tools playwright
npx --prefix artifacts/browser-tools playwright install chromium
node tests/demo.browser.cjs
node tests/multi_application.browser.cjs
```

The check submits only to the simulated destination, verifies retries across reloads, and captures desktop and mobile screenshots under `artifacts/`.

The multi-application check starts one unconfigured console agent and two independent hosts. It verifies automatic enrollment, another user in a fresh browser profile, separate credentials, restart persistence, retry safety and printing while the other host is offline. It uses only a simulated TCP destination.

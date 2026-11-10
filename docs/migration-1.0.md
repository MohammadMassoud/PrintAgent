# Upgrade from 0.x to direct WebSocket printing

Upgrade the NuGet package and Windows service together. Protocol 1 agents cannot accept protocol 2 requests. Version 1.1.0 deliberately does not migrate remote jobs into the new local queue.

1. Stop creating jobs through the old integration. Reconcile pending, claimed and uncertain server jobs. Let known work finish and inspect printers for uncertain results.
2. Back up the server printing database and Data Protection keys. Preserve workstation ProgramData, application profiles and journals. Do not delete these files or copy work into a new queue.
3. Install agent 1.1.0 through the normal installer. The same service is upgraded. Legacy API workers are disabled by default; old server jobs are not fetched in direct mode.
4. Update consuming projects to NuGet 1.1.0. Keep `AddPrintAgent()` and `MapPrintAgent()`. Remove printing-specific API URLs and server data-folder settings; the default integration serves only its browser module. Preserve ordinary host authentication.
5. Use a new browser-storage namespace for direct-mode request records. Never restore old remote request payloads into the local queue: the two databases do not share idempotency records.
6. Keep the tray application running. StartAsync requests native authorization automatically; approve the exact origin in Windows. Run one clearly identified new test before normal operation.

Old APIs and client types remain available for explicit maintenance. `Agent:EnableLegacyApi=true` opts into remote polling and may resume old work. Assess that queue before enabling it. This flag is not required for WebSocket printing.

`UnlinkAsync` now only disconnects the component. To revoke a site's machine-wide permission, use `RevokeAuthorizationAsync()` and approve removal in Windows. Accepted jobs are not cancelled by revocation. Uninstalling preserves ProgramData and approvals; reinstalling is not a queue reset.

## Rollback

Stop submissions and reconcile the local queue first. Keep local and server queues separate. Downgrading does not migrate jobs back. Never run two service processes against one data directory or automatically replay uncertain work during rollback.


## Desktop application (1.1.0)

The installer creates a **SinergiaAI PrintAgent** Start menu shortcut and starts its tray application at Windows sign-in. Double-click its icon to open status and service controls. Opening the Start shortcut brings an existing instance forward and attempts to start a stopped service. Windows administrator consent is required for service start/stop.

Closing the window hides it to the tray. **Exit tray app** removes only the icon. **Stop service and exit** stops printing and closes the tray app, preserving pending work. Interrupted delivery remains Unknown and is not automatically resent. Website permissions can be removed in **Authorized websites**.

Desktop menus and permission windows follow the current Windows user's display language: English for English Windows; Portuguese otherwise. Public SDK identifiers, comments and error contracts remain English. The tray's local named-pipe checks do not poll any application API. StartAsync requests a native decision but never authorizes a website itself.

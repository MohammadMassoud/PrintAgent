# Upgrade from 0.x to direct WebSocket printing

Upgrade the NuGet package and Windows service together. Protocol 1 agents cannot accept protocol 2 requests. Version 1.0.0 deliberately does not migrate remote jobs into the new local queue.

1. Stop creating jobs through the old integration. Reconcile pending, claimed and uncertain server jobs. Let known work finish and inspect printers for uncertain results.
2. Back up the server printing database and Data Protection keys. Preserve workstation ProgramData, application profiles and journals. Do not delete these files or copy work into a new queue.
3. Install agent 1.0.0 through the normal installer. The same service is upgraded. Legacy API workers are disabled by default; old server jobs are not fetched in direct mode.
4. Update consuming projects to NuGet 1.0.0. Keep `AddPrintAgent()` and `MapPrintAgent()`. Remove printing-specific API URLs and server data-folder settings; the default integration serves only its browser module. Preserve ordinary host authentication.
5. Use a new browser-storage namespace for direct-mode request records. Never restore old remote request payloads into the local queue: the two databases do not share idempotency records.
6. Add an `AuthorizeAsync()` button for OriginDenied, approve the exact origin in Windows and refresh. Run one clearly identified new test before normal operation.

Old APIs and client types remain available for explicit maintenance. `Agent:EnableLegacyApi=true` opts into remote polling and may resume old work. Assess that queue before enabling it. This flag is not required for WebSocket printing.

`UnlinkAsync` now only disconnects the component. To revoke a site's machine-wide permission, use `RevokeAuthorizationAsync()` and approve removal in Windows. Accepted jobs are not cancelled by revocation. Uninstalling preserves ProgramData and approvals; reinstalling is not a queue reset.

## Rollback

Stop submissions and reconcile the local queue first. Keep local and server queues separate. Downgrading does not migrate jobs back. Never run two service processes against one data directory or automatically replay uncertain work during rollback.

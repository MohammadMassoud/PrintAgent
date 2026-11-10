using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace SinergiaAI.PrintAgent.Agent;

/// <summary>Only local Windows processes can approve an origin; no browser endpoint can grant access.</summary>
public sealed class LocalApproval(AgentOptions options, LocalPrintQueue queue, AuthorizationRequests requests, ILogger<LocalApproval> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.LocalBridgePort == 0) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            var acl = new PipeSecurity();
            acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.ReadWrite, AccessControlType.Deny));
            acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
            acl.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User!, PipeAccessRights.FullControl, AccessControlType.Allow));
            acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
            using var pipe = NamedPipeServerStreamAcl.Create("SinergiaAI.PrintAgent.Approval." + options.LocalBridgePort,
                PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, acl);
            await pipe.WaitForConnectionAsync(stoppingToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken); timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                var buffer = new byte[2048]; var length = 0;
                while (length < buffer.Length)
                {
                    var count = await pipe.ReadAsync(buffer.AsMemory(length,1), timeout.Token);
                    if (count == 0 || buffer[length++] == 10) break;
                }
                if (length == buffer.Length) throw new InvalidDataException("Approval request exceeded its limit.");
                var request = JsonSerializer.Deserialize<ApprovalRequest>(buffer.AsSpan(0,length)) ?? throw new InvalidDataException();
                string response;
                if (request.Operation == "poll")
                {
                    AuthorizationRequests.Request? pending;
                    while ((pending = requests.Claim()) is not null && queue.IsApproved(pending.Origin))
                        requests.Resolve(pending.Id,pending.Origin,true);
                    response = JsonSerializer.Serialize(pending);
                }
                else if (request.Operation == "origins")
                    response = JsonSerializer.Serialize(queue.ListOrigins());
                else
                {
                    var origin = LocalPrintQueue.NormalizeOrigin(request.Origin ?? "");
                    if (request.RequestId is { } id && !requests.Resolve(id,origin,request.Approve))
                        throw new InvalidDataException("The authorization request expired.");
                    queue.SetOrigin(origin,request.Approve);
                    response = "OK";
                }
                await pipe.WriteAsync(System.Text.Encoding.UTF8.GetBytes(response + "\n"), timeout.Token);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            { logger.LogWarning("Local approval request rejected ({Kind}).", ex.GetType().Name); }
        }
    }
    public sealed record ApprovalRequest(string? Origin = null, bool Approve = false, string? Operation = null, Guid? RequestId = null);
}

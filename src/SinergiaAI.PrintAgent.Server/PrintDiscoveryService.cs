using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using SinergiaAI.PrintAgent.Contracts;

namespace SinergiaAI.PrintAgent.Server;

public sealed record PrintMachineProof([property: JsonPropertyName("comprovante")] string Proof, Guid InstallationId, string MachineName)
{
    // Retain the legacy JSON name and accessor for existing 0.3/0.4 integrations.
    [JsonIgnore, Obsolete("Use Proof instead.")]
    public string Comprovante => Proof;
}
public sealed record PrintMachineConnection(InstallationInfo Installation, string MachineName);
public sealed record ConfirmPrintMachineRequest(string Challenge, Guid InstallationId, string MachineName);

/// <summary>Proves access to a local Windows agent without exposing its permanent token to the browser.</summary>
public sealed class PrintDiscoveryService(PrintStore store, IDataProtectionProvider protection)
{
    private readonly IDataProtector _protector = protection.CreateProtector("PrintAgent.MachineCredentials.v1");
    private sealed record Credentials(string Token, string MachineName);
    private static string RandomCode() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private static bool ValidCode(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    public string CreateChallenge()
    {
        var code = RandomCode();
        store.AddDiscoveryChallenge(PrintStore.Hash(code), DateTimeOffset.UtcNow.AddMinutes(2));
        return code;
    }

    public bool Confirm(ConfirmPrintMachineRequest request, string token)
    {
        if (!ValidCode(request.Challenge) || request.MachineName is not { Length: > 0 and <= 100 }
            || request.MachineName.Any(char.IsControl) || !store.Authenticate(request.InstallationId, token)) return false;
        var encrypted = _protector.Protect(JsonSerializer.Serialize(new Credentials(token, request.MachineName)));
        return store.ConfirmDiscovery(PrintStore.Hash(request.Challenge), request.InstallationId, encrypted);
    }

    public PrintMachineProof? RedeemChallenge(string challenge, string? existingProof = null)
    {
        if (!ValidCode(challenge)) return null;
        var id = store.ConsumeDiscovery(PrintStore.Hash(challenge));
        if (id is not null && GetMachine(existingProof) is { } existing && existing.Installation.Id == id)
            return new(existingProof!, id.Value, existing.MachineName);
        return id is { } installationId ? IssueProof(installationId) : null;
    }

    // Trusted backend operation, also used to migrate a previously configured installation.
    public PrintMachineProof RegisterMachine(InstallationCredentials credentials, string name)
    {
        if (!store.Authenticate(credentials.InstallationId, credentials.AgentToken)) throw new UnauthorizedAccessException();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || name.Any(char.IsControl)) throw new ArgumentException("Invalid machine name.");
        store.SaveMachine(credentials.InstallationId, _protector.Protect(JsonSerializer.Serialize(new Credentials(credentials.AgentToken, name))));
        return IssueProof(credentials.InstallationId);
    }

    private PrintMachineProof IssueProof(Guid id)
    {
        var credentials = ReadCredentials(id) ?? throw new InvalidOperationException("Machine credentials unavailable.");
        var ticket = RandomCode();
        store.AddMachineGrant(PrintStore.Hash(ticket), id, DateTimeOffset.UtcNow.AddDays(365));
        return new(ticket, id, credentials.MachineName);
    }

    private Credentials? ReadCredentials(Guid id)
    {
        var encrypted = store.ReadMachine(id);
        if (encrypted is null) return null;
        try { return JsonSerializer.Deserialize<Credentials>(_protector.Unprotect(encrypted)); }
        catch (CryptographicException) { return null; }
        catch (JsonException) { return null; }
    }

    public PrintMachineConnection? GetMachine(string? proof)
    {
        var id = ValidCode(proof) ? store.FindMachineGrant(PrintStore.Hash(proof!)) : null;
        if (id is null || ReadCredentials(id.Value) is not { } credentials || store.GetInstallation(id.Value) is not { } installation) return null;
        return new(installation, credentials.MachineName);
    }

    public InstallationCredentials GetCredentials(string proof)
    {
        var machine = GetMachine(proof) ?? throw new UnauthorizedAccessException("Machine proof invalid or expired.");
        var credentials = ReadCredentials(machine.Installation.Id)!;
        return new(machine.Installation.Id, credentials.Token);
    }

    public void ForgetProof(string proof)
    {
        if (ValidCode(proof)) store.RemoveMachineGrant(PrintStore.Hash(proof));
    }
}

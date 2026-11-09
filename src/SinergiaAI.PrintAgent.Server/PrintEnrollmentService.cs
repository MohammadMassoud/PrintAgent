using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using SinergiaAI.PrintAgent.Contracts;

namespace SinergiaAI.PrintAgent.Server;

public sealed record InstallerContext(string ApiBaseUrl, string Code, DateTimeOffset ExpiresAt);

public sealed class PrintEnrollmentService(PrintStore store, IDataProtectionProvider protection)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IDataProtector _protector = protection.CreateProtector("SinergiaPrint.Enrollment.v1");

    public byte[] CreateInstaller(InstallationCredentials credentials, Uri apiBaseUrl)
        => BuildInstaller(CreateCode(credentials, apiBaseUrl));

    public InstallerContext CreateCode(InstallationCredentials credentials, Uri apiBaseUrl)
    {
        if ((apiBaseUrl.Scheme != "https" && !(apiBaseUrl.Scheme == "http" && apiBaseUrl.IsLoopback)) ||
            apiBaseUrl.UserInfo.Length != 0 || apiBaseUrl.Query.Length != 0 || apiBaseUrl.Fragment.Length != 0)
            throw new ArgumentException("Use HTTPS (HTTP only on loopback), without credentials in the API URL.");
        if (!store.Authenticate(credentials.InstallationId, credentials.AgentToken))
            throw new UnauthorizedAccessException("Invalid installation.");
        var api = apiBaseUrl.AbsoluteUri.TrimEnd('/') + "/";
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expiry = DateTimeOffset.UtcNow.AddMinutes(30);
        var configuration = new AgentSetupConfiguration(api, credentials.InstallationId, credentials.AgentToken);
        store.SaveEnrollment(PrintStore.Hash(code), _protector.Protect(JsonSerializer.Serialize(configuration, Json)), expiry);
        return new(api, code, expiry);
    }

    public AgentSetupConfiguration? Redeem(string? code)
    {
        if (code is null || code.Length != 64 || !code.All(Uri.IsHexDigit)) return null;
        var encrypted = store.ConsumeEnrollment(PrintStore.Hash(code));
        if (encrypted is null) return null;
        try
        {
            var configuration = JsonSerializer.Deserialize<AgentSetupConfiguration>(_protector.Unprotect(encrypted), Json);
            return configuration is not null && store.Authenticate(configuration.InstallationId, configuration.Token)
                ? configuration : null;
        }
        catch (CryptographicException) { return null; }
        catch (JsonException) { return null; }
    }

    private static byte[] BuildInstaller(InstallerContext context)
    {
        using var template = typeof(PrintEnrollmentService).Assembly.GetManifestResourceStream("SinergiaAI.PrintAgent.Bootstrap.exe")
            ?? throw new InvalidOperationException("The installer bootstrap was not embedded in this build.");
        using var output = new MemoryStream();
        template.CopyTo(output);
        var contextBytes = JsonSerializer.SerializeToUtf8Bytes(context, Json);
        output.Write(contextBytes);
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        writer.Write(contextBytes.Length);
        writer.Write(Encoding.ASCII.GetBytes("SINERGIAPRINT-CONTEXT-V1"));
        writer.Flush();
        return output.ToArray();
    }
}

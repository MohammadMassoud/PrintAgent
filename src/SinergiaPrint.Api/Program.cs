using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using SinergiaPrint.Server;
using SinergiaPrint.Contracts;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 3 * 1024 * 1024);
var apiKey = builder.Configuration["Printing:ApiKey"];
if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length < 32) throw new InvalidOperationException("Configure Printing:ApiKey com pelo menos 32 caracteres.");
builder.Services.AddSinergiaPrintServer(options => options.DatabasePath = builder.Configuration["Printing:DatabasePath"] ?? "data/printing.db");
var app = builder.Build();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        var supplied = context.Request.Headers["X-Api-Key"].ToString();
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(supplied)), SHA256.HashData(Encoding.UTF8.GetBytes(apiKey))))
        { context.Response.StatusCode = 401; return; }
    }
    await next(context);
});
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapPost("/api/installations", (CreateInstallationRequest request, PrintStore store) =>
    string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 160 ? Results.BadRequest(new { error = "Informe um nome de ate 160 caracteres." }) : Results.Ok(store.CreateInstallation(request.Name.Trim())));
app.MapGet("/api/installations", (PrintStore store) => store.Installations());
app.MapGet("/api/installations/{id:guid}/printers", (Guid id, PrintStore store) =>
{
    var installation = store.Installations().Find(x => x.Id == id);
    return installation is null ? Results.NotFound() : Results.Ok(installation.Printers);
});
app.MapPost("/api/jobs", (SubmitPrintRequest request, PrintStore store) =>
{
    if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 200 || string.IsNullOrWhiteSpace(request.PrinterId) || string.IsNullOrWhiteSpace(request.ContentBase64)) return Results.BadRequest(new { error = "Informe impressora, conteudo e chave de idempotencia (ate 200 caracteres)." });
    try { var bytes = Convert.FromBase64String(request.ContentBase64); if (bytes.Length is 0 or > 2097152) return Results.BadRequest(new { error = "Conteudo deve ter de 1 byte a 2 MiB." }); }
    catch (FormatException) { return Results.BadRequest(new { error = "Conteudo Base64 invalido." }); }
    var installation = store.Installations().Find(x => x.Id == request.InstallationId);
    if (installation is null || !installation.Printers.Any(x => x.Id == request.PrinterId)) return Results.BadRequest(new { error = "Impressora nao informada por esta instalacao." });
    try { var job = store.Submit(request); return Results.Accepted($"/api/jobs/{job.Id}", job); }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
});
app.MapGet("/api/jobs/{id:guid}", (Guid id, PrintStore store) => store.GetJob(id) is { } job ? Results.Ok(job) : Results.NotFound());
app.MapSinergiaPrintAgent();
app.Run();

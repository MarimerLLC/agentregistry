using MarimerLLC.AgentRegistry.Api.ApiKeys;
using MarimerLLC.AgentRegistry.Api.Agents;
using MarimerLLC.AgentRegistry.Api.Auth;
using MarimerLLC.AgentRegistry.Api.Protocols.A2A;
using MarimerLLC.AgentRegistry.Api.Protocols.ACP;
using MarimerLLC.AgentRegistry.Api.Protocols.MCP;
using MarimerLLC.AgentRegistry.Api.Protocols.QueuedA2A;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MarimerLLC.AgentRegistry.Application.Agents;
using MarimerLLC.AgentRegistry.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ── Serilog ────────────────────────────────────────────────────────────────────
// Reads from appsettings.json ("Serilog" section) and always writes to console.
// In production, add additional sinks (e.g. Serilog.Sinks.OpenTelemetry) via configuration.
builder.Host.UseSerilog((ctx, services, config) =>
{
    config
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "AgentRegistry")
        .WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
});

// ── Configuration ──────────────────────────────────────────────────────────────
bool isProduction = builder.Environment.IsProduction();

var pgConn = builder.Configuration.GetConnectionString("Postgres")
    ?? (isProduction ? throw new InvalidOperationException("Missing connection string 'Postgres'.") : "Host=localhost;Database=agentregistry_test");

var redisConn = builder.Configuration.GetConnectionString("Redis")
    ?? (isProduction ? throw new InvalidOperationException("Missing connection string 'Redis'.") : "localhost:6379");

// ── OpenTelemetry ──────────────────────────────────────────────────────────────
var otelEndpoint = builder.Configuration["Otel:Endpoint"];

builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(r => r
        .AddService("AgentRegistry", serviceVersion: "1.0.0")
        .AddTelemetrySdk())
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();

        if (builder.Environment.IsDevelopment())
            tracing.AddConsoleExporter();

        if (!string.IsNullOrWhiteSpace(otelEndpoint))
            tracing.AddOtlpExporter(opt =>
            {
                opt.Endpoint = new Uri(otelEndpoint);
                opt.Protocol = OtlpExportProtocol.Grpc;
            });
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();

        if (!string.IsNullOrWhiteSpace(otelEndpoint))
            metrics.AddOtlpExporter(opt =>
            {
                opt.Endpoint = new Uri(otelEndpoint);
                opt.Protocol = OtlpExportProtocol.Grpc;
            });
    });

// ── Services ───────────────────────────────────────────────────────────────────
builder.Services.AddInfrastructure(pgConn, redisConn,
    cfg => builder.Configuration.GetSection("AgentSeeds").Bind(cfg));
builder.Services.AddScoped<AgentService>();

// MCP server — exposes registry discovery as MCP tools at POST/GET /mcp
builder.Services.AddMcpServer(opts =>
    {
        opts.ServerInfo = new Implementation { Name = "AgentRegistry", Version = "1.0.0" };
    })
    .WithHttpTransport()
    .WithTools<AgentRegistryMcpTools>();

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = "Smart";
    options.DefaultChallengeScheme = "Smart";
})
.AddPolicyScheme("Smart", "API Key or JWT", opts =>
{
    opts.ForwardDefaultSelector = ctx =>
        ctx.Request.Headers.ContainsKey("X-Api-Key")
            ? ApiKeyScheme.Name
            : JwtBearerDefaults.AuthenticationScheme;
})
.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
    ApiKeyScheme.Name, _ => { })
.AddJwtBearer(opts => builder.Configuration.GetSection("Jwt").Bind(opts));

builder.Services.AddAuthorization(RegistryPolicies.Configure);

builder.Services.AddHealthChecks()
    .AddInfrastructureHealthChecks(pgConn);

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "Agent Registry API";
        document.Info.Description = "Protocol-agnostic registry for A2A, MCP, and ACP agents with multi-transport support.";
        document.Info.Version = "v1";
        return Task.CompletedTask;
    });
});

// ── Pipeline ───────────────────────────────────────────────────────────────────
var app = builder.Build();

app.UseSerilogRequestLogging(opts =>
{
    opts.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000}ms";
});

app.MapOpenApi();
app.MapScalarApiReference(opts =>
{
    opts.Title = "Agent Registry";
    opts.Theme = ScalarTheme.DeepSpace;
    opts.DefaultHttpClient = new(ScalarTarget.CSharp, ScalarClient.HttpClient);
});

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/healthz", new HealthCheckOptions
{
    Predicate = check => !check.Tags.Contains("ready")
});
app.MapHealthChecks("/readyz", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

// MCP Streamable HTTP endpoint — POST/GET /mcp (spec 2025-11-25)
app.MapMcp("/mcp");

app.MapAgentEndpoints();
app.MapDiscoveryEndpoints();
app.MapApiKeyEndpoints();
app.MapA2AEndpoints();
app.MapMcpEndpoints();
app.MapAcpEndpoints();
app.MapQueuedA2AEndpoints();

if (builder.Configuration.GetValue<bool>("Database:AutoMigrate"))
    await app.Services.MigrateAsync();

app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program;

public static class ApiKeyScheme
{
    public const string Name = "ApiKey";
}

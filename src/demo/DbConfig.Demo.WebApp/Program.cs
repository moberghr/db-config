// Demo host for DbConfig. Wires AddDbConfig -> SQL Server, MapDbConfigHttp, MapDbConfigUi.
// NOT FOR PRODUCTION: uses a static API-key middleware as the auth gateway.

using System.Security.Claims;
using System.Text.Encodings.Web;
using DbConfig.Core;
using DbConfig.Demo.WebApp;
using DbConfig.EntityFrameworkCore;
using DbConfig.Http;
using DbConfig.Provider.SqlServer;
using DbConfig.Ui;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// --- DbConfig wireup (single-call) ---
var connectionString = builder.Configuration.GetConnectionString("DbConfig")
    ?? throw new InvalidOperationException("ConnectionStrings:DbConfig is required.");

builder.Services.AddHttpContextAccessor();

builder.AddDbConfig(b =>
{
    b.Options.AppName = "DbConfigDemo";
    b.Options.Environment = builder.Environment.EnvironmentName;
    b.Options.ReloadInterval = TimeSpan.FromSeconds(10);
    b.UseSqlServer(connectionString);
    b.AddTenantResolver<DemoTenantResolver>();
});

// Register options the standard way — IOptionsSnapshot<T> is tenant-aware automatically.
builder.Services.Configure<DemoTenantOptions>(builder.Configuration.GetSection("DemoTenant"));

// --- Auth: demo API-key gateway (NOT FOR PROD) ---
const string AdminPolicy = "DbConfigAdmin";
const string ApiKeyScheme = "ApiKey";

builder.Services
    .AddAuthentication(ApiKeyScheme)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyHandler>(ApiKeyScheme, null);

builder.Services.AddAuthorization(o =>
    o.AddPolicy(AdminPolicy, p => p.RequireAuthenticatedUser()));

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Apply EF migrations on startup (demo only — NOT FOR PRODUCTION).
// Creates the DbConfig_Entries table and schema if absent.
var migrateOptions = new DbContextOptionsBuilder<DbConfigDbContext>()
    .UseSqlServer(
        connectionString,
        sql => sql.MigrationsAssembly("DbConfig.Provider.SqlServer"))
    .Options;

await using (var ctx = new DbConfigDbContext(migrateOptions))
{
    await ctx.Database.MigrateAsync();
}

// --- Endpoints ---
app.MapGet("/", () =>
    "DbConfig demo host. UI at /admin/dbconfig (X-Db-Config-Api-Key required). API at /api/dbconfig.");

app.MapDbConfigHttp("/api/dbconfig").RequireAuthorization(AdminPolicy);

// ============================================================================
// DEMO ONLY — UI is NOT behind RequireAuthorization. In production, mount it
// inside a RequireAuthorization group OR a reverse-proxy auth gate.
// ============================================================================
app.MapDbConfigUi("/admin/dbconfig", "/api/dbconfig");

// Read-only demo endpoint — no API key required.
// Uses IOptionsSnapshot<T> which is scoped per-request; TryGet on the configuration
// provider resolves the current tenant via DemoTenantResolver automatically.
app.MapGet("/demo/whoami", (
    ITenantResolver resolver,
    IOptionsSnapshot<DemoTenantOptions> opts) =>
{
    var tenantId = resolver.Resolve() ?? string.Empty;
    return Results.Ok(new
    {
        CurrentTenant = string.IsNullOrEmpty(tenantId) ? "(none — global defaults)" : tenantId,
        Options = opts.Value,
    });
});

await app.RunAsync();

// --- Tenant resolver — reads X-Tenant-Id header (NOT FOR PROD) ---
// Real hosts extract tenant identity from JWT claims, route values, subdomains, etc.
internal sealed class DemoTenantResolver : ITenantResolver
{
    private readonly IHttpContextAccessor _httpContext;

    public DemoTenantResolver(IHttpContextAccessor httpContext) => _httpContext = httpContext;

    public string? Resolve()
    {
        var ctx = _httpContext.HttpContext;
        if (ctx is null)
        {
            return null;
        }

        return ctx.Request.Headers.TryGetValue("X-Tenant-Id", out var v) ? v.ToString() : null;
    }
}

// --- API key auth handler (NOT FOR PROD) ---
internal sealed class ApiKeyHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration configuration)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Context.Request.Headers.TryGetValue("X-Db-Config-Api-Key", out var provided))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var expected = configuration["DbConfigDemo:AdminApiKey"];
        if (string.IsNullOrEmpty(expected) || !string.Equals(provided, expected, StringComparison.Ordinal))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "demo-admin")],
            Scheme.Name);
        var principal = new ClaimsPrincipal(identity);

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(principal, Scheme.Name)));
    }
}

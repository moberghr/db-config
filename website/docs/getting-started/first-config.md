---
sidebar_position: 2
---

# First config

This page walks you through registering DbConfig, writing your first entry, and reading it
back via `IConfiguration` and `IOptionsMonitor<T>`.

## Register DbConfig

### In a web application (`WebApplicationBuilder`)

```csharp
var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DbConfig")
    ?? throw new InvalidOperationException("ConnectionStrings:DbConfig is required.");

builder.AddDbConfig(b =>
{
    b.Options.AppName = "MyApp";
    b.Options.Environment = builder.Environment.EnvironmentName;
    b.Options.ReloadInterval = TimeSpan.FromSeconds(30);
    b.UseSqlServer(connectionString); // or b.UsePostgreSql(connectionString)
});

// Unified admin surface: UI + HTTP API under one prefix with one cookie.
builder.Services.AddScoped<IDbConfigCredentialValidator, MyValidator>();

var app = builder.Build();

app.MapDbConfigAdmin("/admin/dbconfig", opts =>
{
    opts.UseBuiltInLogin<MyValidator>();
});
// → UI at  /admin/dbconfig
// → API at /admin/dbconfig/api

await app.RunAsync();
```

:::tip Authentication
`MapDbConfigAdmin`, `MapDbConfigHttp`, and `MapDbConfigUi` are all **open by
default** and return `RouteGroupBuilder`. Hosts have five options — open
access, compose with the existing pipeline via `RequireAuthorization`, use
the unified `MapDbConfigAdmin` + built-in cookie login (recommended), split
prefixes with `opts.UseBuiltInLogin<T>()`, or plug in a custom
`IDbConfigAuthorizationFilter`. See [Authentication & authorization](../configuration/auth.md).
:::

### In a worker service (`HostApplicationBuilder`)

The same single call works for a generic host. `AddDbConfig` is an extension on
`IHostApplicationBuilder`, which both `WebApplicationBuilder` and `HostApplicationBuilder`
implement:

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.AddDbConfig(b =>
{
    b.Options.AppName = "MyWorker";
    b.Options.Environment = builder.Environment.EnvironmentName;
    b.UseSqlServer(connectionString);
});

var host = builder.Build();
await host.RunAsync();
```

Worker services do not map HTTP endpoints. The configuration provider polls the store and
fires `IChangeToken` whenever values change — no HTTP stack required.

:::warning
The connection string must be resolvable before `AddDbConfig` is called. If it is missing,
`AddDbConfig` throws `InvalidOperationException` immediately. DbConfig does not silently
return empty values when the database is unreachable at startup.
:::

## Write your first entry via the HTTP API

With the app running, write a configuration entry using `curl`:

```bash
curl -X PUT http://localhost:5000/admin/dbconfig/api/MyApp/Development/Database:ConnectionString \
  -H "Content-Type: application/json" \
  -b "dbconfig-auth=$COOKIE" \
  -d '{"value": "Server=localhost;Database=mydb;Integrated Security=true", "isSecret": true}'
```

The route format is `/{appName}/{environment}/{*key}` under the configured API prefix
(`/admin/dbconfig/api` when using `MapDbConfigAdmin`). The key segment uses `:` as the
hierarchy separator (matching `IConfiguration` convention). Forward slashes in the URL
path are normalized to `:` automatically, so
`/admin/dbconfig/api/MyApp/Development/Database/ConnectionString` is equivalent to the
example above.

The request body:

```json
{
  "value": "the value to store",
  "isSecret": true
}
```

Set `isSecret: true` for connection strings, API keys, passwords, and OAuth secrets.
Set `isSecret: false` for feature flags, log levels, URLs, and other non-sensitive config.

A successful `PUT` returns `204 No Content` and immediately fires the in-process reload
signal so subscribers see the new value without waiting for the next poll.

## Read the value via `IConfiguration`

Configuration values flow through the standard `IConfiguration` abstraction:

```csharp
// In a request handler or hosted service — resolves after host.Build()
app.MapGet("/debug/connection", (IConfiguration config) =>
    config["Database:ConnectionString"]);
```

A key stored as `Database:ConnectionString` is accessible as either:

```csharp
config["Database:ConnectionString"]                  // flat access
config.GetSection("Database")["ConnectionString"]    // section access
```

## Read via `IOptionsMonitor<T>` with reload callbacks

For reactive reload, bind a section to a POCO via `IOptionsMonitor<T>`. The monitor fires
`OnChange` whenever DbConfig reloads:

```csharp
// Settings POCO
public sealed class DatabaseSettings
{
    public string ConnectionString { get; set; } = string.Empty;
}

// In Program.cs
builder.Services.Configure<DatabaseSettings>(
    builder.Configuration.GetSection("Database"));

// In a background service
public class MyWorker(IOptionsMonitor<DatabaseSettings> settings, ILogger<MyWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        settings.OnChange(s =>
            logger.LogInformation("Connection string updated — reconnecting"));

        while (!stoppingToken.IsCancellationRequested)
        {
            var connStr = settings.CurrentValue.ConnectionString;
            // ... use connStr ...
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
```

## Reload semantics

Changes propagate via two paths:

1. **HTTP API mutation (`PUT` or `DELETE`)** — the endpoint calls
   `IDbConfigReloadSignal.Trigger()` immediately after writing to the store. In-process
   consumers reload within milliseconds.

2. **Poll interval expiry** — the provider checks `GetLatestModifiedUtcAsync` every
   `ReloadInterval` (default 30 seconds). If any row's `ModifiedUtc` advanced, it fetches
   all entries and fires `IChangeToken`.

Both paths trigger the same `OnReload()` → `IChangeToken` → `IOptionsMonitor.OnChange`
chain. There is no observable difference from the consumer's perspective.

:::note
Direct SQL mutations (`INSERT`, `UPDATE`, `DELETE` against `DbConfig_Entries`) bypass the
reload signal. The polling provider only sees them when a subsequent row's `ModifiedUtc`
advances. Always mutate through the HTTP API or the UI editor.
:::

## Next steps

- [Single-call DI](../configuration/single-call-di.md) — full reference for `AddDbConfig` options
- [Scopes](../configuration/scopes.md) — share config across multiple applications
- [Encryption](../configuration/encryption.md) — configure persistent Data Protection keys
- [HTTP API endpoints](../http-api/endpoints.md) — full endpoint reference with curl examples

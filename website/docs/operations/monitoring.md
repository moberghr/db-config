---
sidebar_position: 4
---

# Monitoring

DbConfig uses standard .NET structured logging via `ILogger<T>`. There is no built-in
metrics surface or health endpoint, but there are specific log events worth watching and
a straightforward pattern for wiring up health checks.

## Log events to watch

### Configuration reload failures

Category: `DbConfig.Core.DbConfigConfigurationProvider`
Level: `Warning`

```
[Warning] DbConfig reload failed — retaining previous values. Next retry in {interval}.
```

Logged when `GetAllAsync` (or `GetAllScopedAsync`) throws during a polling tick. The
provider keeps the previous values and retries on the next tick. This typically means the
database is temporarily unreachable.

**Action:** check database connectivity. If the error persists for multiple intervals,
investigate the connection string and network path.

### Reload signal resolution error

Category: `DbConfig.Core.DbConfigRegistrationMarker`
Level: `Error`

```
IDbConfigReloadSignal resolved before the configuration source was loaded.
```

Rare. Indicates that something in your startup code is resolving `IDbConfigReloadSignal`
before `host.Build()` completes. Move the resolution to a request handler or hosted
service.

### Missing audit store when `AuditReads = true`

Category: `DbConfig.Http.Endpoints.QueryEntriesEndpointMarker` (or `GetEntryEndpointMarker`)
Level: `Warning` (logged once per endpoint, not per request)

```
DbConfigOptions.AuditReads is true, but IConfigAuditStore is not registered.
Read audit rows will not be written.
```

Indicates that `AuditReads = true` was set but the EF Core provider was not wired up (or a
custom store was registered that doesn't implement `IConfigAuditStore`). The standard EF
providers register `IConfigAuditStore` automatically; this warning should not appear in a
standard setup.

### Pre-build secret read with type-mapped encryptor

Category: varies (thrown from `DbConfigConfigurationProvider.TryGet`)
Level: `Exception` (throws `InvalidOperationException`)

```
Cannot read secret value 'X' before host.Build() has returned.
Move this read to a request handler, hosted service, or OnStarted callback.
```

Thrown when code reads a secret config value before `host.StartAsync()` has run the
`DbConfigEncryptorActivator` hosted service. Non-secret reads are unaffected.

**Action:** move the config read to a request handler, `IHostedService.StartAsync`, or
`IOptions<T>` binding (which is evaluated lazily post-build).

### First-load failure

Level: `Error` (throws `InvalidOperationException` on startup)

```
DbConfig: connection string 'X' is required but was not found.
```

The connection string passed to `UseSqlServer` / `UsePostgreSql` was null or empty at
registration time. The provider does not silently return empty values — it throws
immediately on startup so the misconfiguration is visible.

## No first-class metrics surface

DbConfig does not expose `System.Diagnostics.Metrics` counters or `EventSource` events
in v0.x. If you need metrics (reload frequency, store query latency, encryption throughput),
add a custom `IConfigStore` decorator:

```csharp
public sealed class InstrumentedConfigStore(IConfigStore inner, IMeterFactory meterFactory) : IConfigStore
{
    private readonly Counter<long> _reloads = meterFactory.Create("dbconfig").CreateCounter<long>("dbconfig.reloads");

    public async Task<IReadOnlyList<ConfigEntry>> GetAllAsync(string scope, string environment, CancellationToken ct)
    {
        _reloads.Add(1);
        return await inner.GetAllAsync(scope, environment, ct);
    }

    // delegate remaining methods to inner ...
}
```

Register the decorator before `AddDbConfig`. First-class metrics are tracked for a future
release.

## Health checks

The polling provider does not register a health check endpoint. To monitor database
connectivity in your ASP.NET Core health endpoint:

```bash
dotnet add package Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore
```

```csharp
builder.Services.AddHealthChecks()
    .AddDbContextCheck<DbConfigDbContext>("dbconfig-db");

app.MapHealthChecks("/health");
```

This pings the database on every health check request. Combine it with your own app's
existing health infrastructure.

Alternatively, use a SQL-level health check directly against the `DbConfig_Entries` table
if you do not want to expose `DbConfigDbContext` to the health check infrastructure.

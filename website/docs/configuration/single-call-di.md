---
sidebar_position: 1
---

# Single-call DI

DbConfig uses a single-call registration pattern: one call to `builder.AddDbConfig(...)` on
`IHostApplicationBuilder` wires everything — services, the configuration source, the polling
provider, and the reload signal. No second call, no bridge dance.

## The current shape

```csharp
builder.AddDbConfig(b =>
{
    b.Options.AppName = "MyApp";
    b.Options.Environment = builder.Environment.EnvironmentName;
    b.Options.ReloadInterval = TimeSpan.FromSeconds(30);
    b.Options.IncludeScopes = ["PlatformDefaults", "Shared"]; // optional
    b.UseSqlServer(connectionString);   // or b.UsePostgreSql(connectionString)
});
```

`AddDbConfig` is defined on `IHostApplicationBuilder` (from `Microsoft.Extensions.Hosting`).
Both `WebApplicationBuilder` and `HostApplicationBuilder` implement this interface in
.NET 8+, so the same call works in ASP.NET Core web apps and background worker services.

## What `AddDbConfig` registers

Internally, `AddDbConfig` runs the user's lambda and then does two things:

**1. Registers the HTTP-side stack into host DI:**

| Registration | Type |
|---|---|
| `DbConfigOptions` | The options instance populated from the lambda |
| `TimeProvider` | `TimeProvider.System` (via `TryAddSingleton`) |
| `IUniqueConstraintDetector` | Provider-specific implementation (SQL Server or PostgreSQL) |
| `IDbContextFactory<DbConfigDbContext>` | EF Core factory for the HTTP layer |
| `IConfigStore` | `EfCoreConfigStore` using the factory above |
| `IConfigAuditStore` | `EfCoreConfigAuditStore` (read/write audit) |
| `IConfigEncryptor` | `DataProtectionConfigEncryptor` (via `TryAddSingleton`) |
| `IDbConfigReloadSignal` | Lazy factory resolving the polling provider from the marker |
| `DbConfigRegistrationMarker` | Internal marker used to locate the source after `Build()` |

**2. Constructs the polling-side stack directly (no DI lookup):**

The polling provider cannot wait for the host's `IServiceProvider` to be built — the
configuration system loads before DI. So `AddDbConfig` builds a private
`DirectDbContextFactory<DbConfigDbContext>` from the same connection string the lambda
provided, creates `new EfCoreConfigStore(factory, detector, TimeProvider.System)` directly,
wraps it in `DbConfigConfigurationSource`, and adds it to `builder.Configuration`.

## Two stores, same database

There are always two `EfCoreConfigStore` instances per host:

- **Polling-side store** — built directly during `AddDbConfig`, used by the
  `DbConfigConfigurationProvider` timer loop
- **HTTP-side store** — registered in DI, used by the HTTP endpoint handlers

Both instances point at the same database connection string. They share no in-process state
by design. The database is the source of truth; the `IDbConfigReloadSignal` (fired by HTTP
write endpoints) coordinates cache invalidation between the two sides.

This design means a `PUT` or `DELETE` via the HTTP API:
1. Writes to the store via the HTTP-side instance
2. Fires `IDbConfigReloadSignal.Trigger()`
3. The polling provider (holding the polling-side store) immediately re-reads from the DB
4. Any `IOptionsMonitor<T>` subscribers are notified

## Single-scope constraint

`AddDbConfig` can only be called once per host. Calling it twice on the same
`IHostApplicationBuilder` throws `InvalidOperationException` immediately:

```
DbConfig is already registered on this host. AddDbConfig can only be called once.
```

This constraint exists because `IDbConfigReloadSignal` resolution walks the single
`DbConfigRegistrationMarker` to locate the polling provider. Supporting multiple
`(AppName, Environment)` pairs from different connection strings in a single host is
tracked for a future release.

## All `DbConfigOptions` fields

```csharp
public sealed class DbConfigOptions
{
    /// <summary>Application name — first component of the (AppName, Environment, Key) triple.</summary>
    public string AppName { get; set; } = string.Empty;

    /// <summary>Environment name — second component of the triple.</summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>Polling interval. Default is 30 seconds.</summary>
    public TimeSpan ReloadInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Additional AppName scopes to include, ordered lowest-precedence-first.
    /// Own AppName always wins. Empty by default.
    /// </summary>
    public IReadOnlyList<string> IncludeScopes { get; set; } = [];

    /// <summary>
    /// Write mutation audit rows in-transaction with every Upsert/Delete. Default true.
    /// </summary>
    public bool EnableAuditLog { get; set; } = true;

    /// <summary>
    /// When true, HTTP GET endpoints write fire-and-forget read audit rows. Default false.
    /// </summary>
    public bool AuditReads { get; set; } = false;
}
```

## Custom encryptor registration

If you need a custom `IConfigEncryptor` (Azure Key Vault, AWS KMS, etc.), register it
**before** calling `AddDbConfig`. The default `TryAddSingleton` registration becomes a
no-op when a prior registration exists:

```csharp
// Instance-registered (encryptor built before AddDbConfig)
builder.Services.AddSingleton<IConfigEncryptor>(new MyAzureKeyVaultEncryptor(client, opts));
builder.AddDbConfig(b => { ... });

// Type-mapped (DI resolves the encryptor after Build)
builder.Services.AddSingleton<IConfigEncryptor, MyAzureKeyVaultEncryptor>();
builder.AddDbConfig(b => { ... });
```

See [Encryption](./encryption.md) for the full story, including the type-mapped caveat
on pre-build secret reads.

## Data Protection key persistence

`AddDbConfig` calls `services.TryAddSingleton<IDataProtectionProvider>` to ensure Data
Protection is available. If you configure key persistence, do it **before** `AddDbConfig`:

```csharp
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo("/var/dbconfig/keys"))
    .ProtectKeysWithCertificate("thumbprint");

builder.AddDbConfig(b => { ... });
```

See [Key persistence](../operations/key-persistence.md) for a full discussion.

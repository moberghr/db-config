---
sidebar_position: 1
---

# Introduction

DbConfig is a database-backed `IConfiguration` provider for .NET 8. It lets you store
configuration values in your existing SQL Server or PostgreSQL database, edit them through
an embedded React UI, and reload them in running processes — no redeploy required.

It is **not** a hosted secrets manager like Azure Key Vault or AWS Secrets Manager. There
is no extra service to run. Everything lives in your app's own database, behind your own
auth policy.

![DbConfig entries list](/img/screenshots/01-entries-list.png)

<details>
<summary>Dark theme</summary>

![DbConfig entries list in dark theme](/img/screenshots/01-entries-list-dark.png)

</details>

## Quick example

Two lines of setup in `Program.cs`:

```csharp
// 1. Single call — wires services, configuration source, and reload signal
builder.AddDbConfig(b =>
{
    b.Options.AppName = "MyApp";
    b.Options.Environment = builder.Environment.EnvironmentName;
    b.Options.ReloadInterval = TimeSpan.FromSeconds(30);
    b.UseSqlServer(connectionString); // or b.UsePostgreSql(connectionString)
});

// 2. Map the JSON API + the embedded editor UI
app.MapDbConfigHttp("/api/dbconfig").RequireAuthorization("DbConfigAdmin");
app.MapDbConfigUi("/admin/dbconfig", "/api/dbconfig");
```

After that, `IConfiguration["MyKey"]` reads from the database. Any change made via the
UI or the HTTP API fires a reload that reaches every `IOptionsMonitor<T>` subscriber in the
same process within milliseconds.

## What you get

- **Per-entry encryption.** Mark an entry `IsSecret = true` and its value is encrypted at
  rest using ASP.NET Core Data Protection (or your own `IConfigEncryptor`). Non-secret
  entries stay plaintext for easy inspection with SSMS or psql.
- **Audit log.** Every mutation (insert, update, delete) writes a row to
  `DbConfig_AuditEntries` in the same database transaction. Opt-in read auditing is
  available for compliance scenarios.
- **Multi-scope configuration.** Pull values from one or more shared scopes (e.g.
  `PlatformDefaults`, `Shared`) in addition to your app's own scope, with explicit
  precedence ordering.
- **Embedded React UI.** One call (`MapDbConfigUi`) mounts a full-featured editor — CRUD,
  secret masking, scope badges, bulk operations, import/export, and an audit history
  diff view.
- **Host-owned authorization.** The packages ship no auth policy. `MapDbConfigHttp` and
  `MapDbConfigUi` return `RouteGroupBuilder` so you attach `RequireAuthorization(...)` the
  same way you would for any other endpoint group.

## What you bring

- **A SQL Server or PostgreSQL database** that your application already has a connection
  string for. DbConfig creates its own tables (`DbConfig_Entries`,
  `DbConfig_AuditEntries`) via EF Core migrations; it does not touch your schema.
- **EF Core 8 migrations applied.** Run `dotnet ef database update` once (or call
  `ctx.Database.MigrateAsync()` at startup). See
  [Migrations](./operations/migrations.md) for details.
- **An ASP.NET Core 8 host.** `AddDbConfig` is an extension on `IHostApplicationBuilder`,
  so it also works in worker services and generic hosts — but `MapDbConfigHttp` and
  `MapDbConfigUi` require the ASP.NET Core middleware pipeline.
- **An identity policy for the admin endpoints.** Your application already has an auth
  stack (JWT, Azure AD, Windows Auth, API keys for dev). Attach your existing policy to
  the groups returned by `MapDbConfigHttp` and `MapDbConfigUi`.

## Where to next

| Goal | Page |
|------|------|
| Add NuGet packages and create the tables | [Installation](./getting-started/installation.md) |
| Register DbConfig in an existing app | [Single-call DI](./configuration/single-call-di.md) |
| Understand the editor UI | [UI Editor overview](./ui-editor/overview.md) |
| See a full working example | [Demo host](./getting-started/demo-host.md) |
| Learn about encryption and key persistence | [Encryption](./configuration/encryption.md) |

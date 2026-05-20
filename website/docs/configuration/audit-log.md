---
sidebar_position: 4
---

# Audit log

DbConfig writes an audit row to `DbConfig_AuditEntries` for every mutation. The audit log
is enabled by default and transactionally guaranteed — audit rows commit atomically with the
entry mutation in the same `SaveChangesAsync` call.

## Mutation audits (always on)

Every `PUT` and `DELETE` request creates a corresponding audit row:

| API action | Audit action | OldValue | NewValue |
|-----------|-------------|----------|---------|
| `PUT` (new key) | `Insert` | null | new value |
| `PUT` (existing key) | `Update` | previous value | new value |
| `DELETE` | `Delete` | previous value | null |

The `Action` field reflects the actual database state transition at commit time, not the
caller's intent. Under a concurrent-insert race, the losing writer's audit row is `Update`
(not `Insert`) — by the time it commits, the row already exists from the winning writer.
This is correct behavior.

Disabling the audit log per-host:

```csharp
builder.AddDbConfig(b =>
{
    b.UseSqlServer(connectionString);
    b.Options.EnableAuditLog = false; // no audit rows, no perf cost
});
```

:::note
Direct SQL mutations (`INSERT`, `UPDATE`, or `DELETE` against `DbConfig_Entries`) bypass
the audit log entirely. The audit log is only as reliable as your discipline about always
going through the API.
:::

## Audit table schema

```sql
-- SQL Server
CREATE TABLE DbConfig_AuditEntries (
    Id            uniqueidentifier  NOT NULL PRIMARY KEY,
    Scope       nvarchar(128)     NOT NULL COLLATE Latin1_General_100_BIN2,
    Environment   nvarchar(64)      NOT NULL COLLATE Latin1_General_100_BIN2,
    Key           nvarchar(512)     NOT NULL COLLATE Latin1_General_100_BIN2,
    OldValue      nvarchar(max)     NULL,
    NewValue      nvarchar(max)     NULL,
    IsSecret      bit               NOT NULL,
    Action        nvarchar(16)      NOT NULL, -- 'Insert' | 'Update' | 'Delete' | 'Read'
    ModifiedUtc   datetime2         NOT NULL,
    ModifiedBy    nvarchar(256)     NULL,
    INDEX IX_AuditEntries_Key (Scope, Environment, Key, ModifiedUtc DESC)
);
```

PostgreSQL uses `text` instead of `nvarchar`, `uuid` instead of `uniqueidentifier`,
`timestamptz` instead of `datetime2`, and `boolean` instead of `bit`. Collation is `"C"`
on `Scope`, `Environment`, and `Key`.

There is no foreign key from `DbConfig_AuditEntries` to `DbConfig_Entries`. Entries can be
deleted; audit rows must survive.

`OldValue` and `NewValue` are stored as ciphertext when `IsSecret = true`. The HTTP audit
history endpoint decrypts them before returning to callers.

## Read auditing (opt-in)

By default, DbConfig only audits mutations. For compliance scenarios that require "who read
which secret?" trails, enable read auditing:

```csharp
builder.AddDbConfig(b =>
{
    b.UseSqlServer(connectionString);
    b.Options.Scope = "PaymentService";
    b.Options.AuditReads = true; // opt-in
});
```

When enabled:

- `GET /{app}/{env}` (list) — writes one audit row with `Key = "*"` (sentinel for
  "entire scope listed")
- `GET /{app}/{env}/{*key}` (single) — writes one audit row with the requested key
- `GET /{app}/{env}/audit/{*key}` (history) — **does not** write a read audit (recursion
  guard)

Read audit rows have `Action = Read`, `OldValue = null`, `NewValue = null`. Even a `404`
(key not found) writes a read audit row — access attempts are recorded, not just successful
reads.

### Fire-and-forget trade-off

Read audit writes are fire-and-forget, not in-transaction. This is intentional: every
`GET` acquiring a database write transaction would double read latency. The trade-off is
"slight chance of missing an audit row on process crash" vs "GET latency stays small."

If a read audit write fails, it logs a warning; the `GET` still returns the value
successfully.

### Read audit row volume

Read audits can dominate the audit table by row count. The list endpoint alone creates one
row per page load. Plan your retention policy accordingly. See
[Audit retention](../operations/audit-retention.md) for recommended SQL and scheduling.

## Cross-recursion guard

The audit history endpoint never generates its own read audit rows, even when
`AuditReads = true`. Reading the audit log would otherwise generate audit rows for reads of
the audit table — infinite recursion. This guard is built into the endpoint handler.

## `ConfigAuditEntry` shape

The audit record type used by `IConfigAuditStore.GetHistoryAsync`:

```csharp
public sealed record ConfigAuditEntry(
    Guid Id,
    string Scope,
    string Environment,
    string Key,
    string? OldValue,    // plaintext after decryption; null on Insert and Read
    string? NewValue,    // plaintext after decryption; null on Delete and Read
    bool IsSecret,
    ConfigAuditAction Action,
    DateTimeOffset ModifiedUtc,
    string? ModifiedBy); // from HttpContext.User?.Identity?.Name at mutation time

public enum ConfigAuditAction { Insert, Update, Delete, Read }
```

## HTTP access

Retrieve audit history for a key:

```bash
GET /api/dbconfig/MyApp/Production/audit/Database:ConnectionString?take=50
```

`take` defaults to 50 and is capped at 500. Results are ordered most-recent-first.

See [Endpoints](../http-api/endpoints.md) for the full reference.

## Retention

The audit log has no built-in pruner. Retention is your responsibility. Recommended
schedule: 90 days for mutations, 30 days for reads.

See [Audit retention](../operations/audit-retention.md) for SQL examples and scheduling
recommendations.

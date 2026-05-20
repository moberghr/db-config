---
sidebar_position: 3
---

# Audit retention

DbConfig ships no built-in pruner. Retaining and pruning the `DbConfig_AuditEntries` table
is your responsibility. This page provides ready-to-use SQL for common retention policies.

## Why no built-in pruner

Retention requirements vary widely by compliance posture:
- Non-regulated workloads may need 30–90 days
- PCI-DSS, HIPAA, and SOX may require 1–7 years
- Read audits (if enabled) generate far more rows than mutation audits

A built-in pruner would impose one policy. Instead, DbConfig gives you the SQL and lets you
schedule it with whatever job runner you already use.

## Recommended policy

For most non-regulated workloads:
- **Mutation audits** (`Insert`, `Update`, `Delete`): keep 90 days
- **Read audits** (`Read`): keep 30 days (much higher row volume)

## SQL Server

```sql
-- Prune read audits: keep 30 days
DELETE FROM DbConfig_AuditEntries
WHERE Action = 'Read'
  AND ModifiedUtc < DATEADD(day, -30, SYSUTCDATETIME());

-- Prune mutation audits: keep 90 days
DELETE FROM DbConfig_AuditEntries
WHERE Action IN ('Insert', 'Update', 'Delete')
  AND ModifiedUtc < DATEADD(day, -90, SYSUTCDATETIME());
```

Run these on a weekly schedule via SQL Server Agent or a hosted service.

## PostgreSQL

```sql
-- Prune read audits: keep 30 days
DELETE FROM "DbConfig_AuditEntries"
WHERE "Action" = 'Read'
  AND "ModifiedUtc" < (NOW() AT TIME ZONE 'UTC' - INTERVAL '30 days');

-- Prune mutation audits: keep 90 days
DELETE FROM "DbConfig_AuditEntries"
WHERE "Action" IN ('Insert', 'Update', 'Delete')
  AND "ModifiedUtc" < (NOW() AT TIME ZONE 'UTC' - INTERVAL '90 days');
```

Schedule via `pg_cron`, a Kubernetes CronJob, or a hosted service in your application.

## Splitting read and mutation pruning

If read auditing (`AuditReads = true`) is enabled, read rows can easily outnumber mutation
rows 100:1 in a busy application. Pruning them more aggressively keeps the table small for
compliance-relevant mutation queries.

To query single-key reads separately from list reads (for compliance tooling):

```sql
-- SQL Server: single-key reads (compliance-relevant for secret access trails)
SELECT * FROM DbConfig_AuditEntries
WHERE Action = 'Read'
  AND [Key] != '*'                    -- exclude list-all sentinels
  AND ModifiedUtc > @since
ORDER BY ModifiedUtc DESC;

-- SQL Server: list-all reads (less compliance-relevant; prune more aggressively)
DELETE FROM DbConfig_AuditEntries
WHERE Action = 'Read'
  AND [Key] = '*'
  AND ModifiedUtc < DATEADD(day, -7, SYSUTCDATETIME());
```

The `Key = '*'` sentinel is written by the flat list endpoint (`GET /`). See
[Audit log](../configuration/audit-log.md) for the full read auditing semantics, and the
[Audit Log page](../ui-editor/audit-log-page.md) for the UI surface.

## Scheduling options

| Platform | Mechanism |
|----------|----------|
| SQL Server | SQL Server Agent job — weekly TSQL step |
| PostgreSQL | `pg_cron` extension — `SELECT cron.schedule(...)` |
| Kubernetes | `CronJob` resource running a SQL script container |
| Application | `IHostedService` or `BackgroundService` with a daily `Timer` |
| Azure | Azure Elastic Jobs or Logic App with an HTTP trigger |

A simple .NET background service example:

```csharp
public class AuditPruner(IDbContextFactory<YourDbContext> factory, ILogger<AuditPruner> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var ctx = await factory.CreateDbContextAsync(stoppingToken);
                await ctx.Database.ExecuteSqlRawAsync(
                    "DELETE FROM DbConfig_AuditEntries WHERE Action = 'Read' AND ModifiedUtc < DATEADD(day, -30, SYSUTCDATETIME())",
                    stoppingToken);
                await ctx.Database.ExecuteSqlRawAsync(
                    "DELETE FROM DbConfig_AuditEntries WHERE Action IN ('Insert','Update','Delete') AND ModifiedUtc < DATEADD(day, -90, SYSUTCDATETIME())",
                    stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Audit pruner failed — will retry next run");
            }

            await Task.Delay(TimeSpan.FromDays(1), stoppingToken);
        }
    }
}
```

Register it with `builder.Services.AddHostedService<AuditPruner>()`. Use your own
`DbContext` (or build a `DbConnection` directly) pointing at the same database — do not
use `DbConfigDbContext` from outside the DbConfig packages.

## Compliance requirements

For regulated workloads (PCI-DSS, HIPAA, SOX), consult your auditor before pruning
mutation audit rows. The standard 90-day policy is a starting point; your actual
requirement may be 1–7 years. Read audits are typically not subject to long retention
requirements.

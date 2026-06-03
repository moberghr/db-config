# Code Index

> Capability index — what the codebase can do, not where files live.
> Refresh: `/mtk audit duplicates` (code-simplification --audit-duplicates).
> Last built: 2026-05-29

## Configuration Provider

| Capability | Entry point | Notes |
|---|---|---|
| Surface DB-backed config as `IConfiguration` | `src/core/DbConfig.Core/DbConfigConfigurationProvider.cs:DbConfigConfigurationProvider` | The provider model — integrate new config-surfacing here, not a parallel read API |
| Register DbConfig via fluent builder | `src/core/DbConfig.Core/DbConfigBuilder.cs:DbConfigBuilder` | Providers extend this (`UseEntityFrameworkCore`/`UsePostgreSql`/`UseSqlServer`); use `TryAdd*` so host overrides win |
| Read tenant-scoped config | `src/core/DbConfig.Core/TenantConfigReader.cs:TenantConfigReader` | First-class `TenantId`; optional fallback to non-tenant values |

## Persistence (EF Core)

| Capability | Entry point | Notes |
|---|---|---|
| EF model for config + audit | `src/core/DbConfig.EntityFrameworkCore/DbConfigDbContext.cs:DbConfigDbContext` | NO name literals in `OnModelCreating` — breaks PostgreSQL snake_case |
| Read/write config store | `src/core/DbConfig.EntityFrameworkCore/EfCoreConfigStore.cs:EfCoreConfigStore` | Reads use `AsNoTracking()` |
| Persist audit rows | `src/core/DbConfig.EntityFrameworkCore/EfCoreConfigAuditStore.cs:EfCoreConfigAuditStore` | Pluggable; degrades gracefully when unregistered |

## Security

| Capability | Entry point | Notes |
|---|---|---|
| Encrypt/decrypt secret values | `src/core/DbConfig.EntityFrameworkCore/DataProtectionConfigEncryptor.cs:DataProtectionConfigEncryptor` | Default real encryptor; never store secrets via the passthrough no-op outside tests |
| Restrict admin UI to loopback | `src/core/DbConfig.Ui/LocalRequestsOnlyAuthorizationFilter.cs:LocalRequestsOnlyAuthorizationFilter` | UI is open by default — wire an auth filter for non-local use |

## HTTP Admin API

| Capability | Entry point | Notes |
|---|---|---|
| Get a config entry | `src/core/DbConfig.Http/Endpoints/GetEntryEndpoint.cs:GetEntryEndpoint` | Minimal-API `HandleAsync`; key normalized `/`→`:`; optional read audit |
| Upsert a config entry | `src/core/DbConfig.Http/Endpoints/UpsertEntryEndpoint.cs:UpsertEntryEndpoint` | Follow the `{Verb}{Entity}Endpoint` pattern for new endpoints |

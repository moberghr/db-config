---
paths:
  - "**"   # repo-wide: secret handling and the auth boundary apply to every file,
           # including samples/ and ui/ where a leaked default would ship to consumers
axes:
  decision: security
  topic: security
  scope: project
---

# Security & Data Integrity

- **§1.1** No secrets, connection strings, or credentials in source code. Use `appsettings.Development.Local.json` (matched by the `**/appsettings.*.Local.json` ignore rule) or environment variables for local dev. **`appsettings.Development.json` is NOT ignored and is tracked** — never put a real credential there. `samples/PaymentsApi/appsettings.json` ships non-secret defaults only — its `Auth:Password` is a demo credential and must never be presented as production-ready.
- **§1.2** No PII or sensitive data in log output. Config values may hold customer data; never log a `ConfigEntry.Value` at Info level or above, and redact connection details in the first-load failure message (AGENTS.md §0.2).
- **§1.3** `IsSecret` drives at-rest encryption via `IConfigEncryptor`, not just UI masking. Mark connection strings, API keys, OAuth client secrets, JWT signing keys, passwords, and third-party tokens. Leave feature flags, log levels, intervals, URLs, and public client IDs plaintext for debuggability. See `project-specific.md` §8.9.
- **§1.4** `IsSecret` is NOT an authorization boundary. Every app whose host includes a scope can read every byte of it in-process. Never put production secrets in a shared scope unless ALL including apps are trusted — `project-specific.md` §8.8.
- **§1.5** Mutation audit rows commit in the same `SaveChangesAsync` as the mutation (AGENTS.md §0.5). Direct SQL `UPDATE`/`INSERT`/`DELETE` on the entries table (`ConfigEntries` on SQL Server, `config_entries` on PostgreSQL) bypasses the store and writes NO audit row — the audit log is only as good as the discipline of always going through the API.
- **§1.6** The packages ship NO `[Authorize]` attributes, NO hard-coded policies, and NO authentication middleware — authorization is opt-in and the host owns identity entirely. `LocalRequestsOnlyAuthorizationFilter` and the built-in cookie login are dev/demo affordances; the consumer-implemented `IDbConfigCredentialValidator` is the security boundary for that flow. Composition patterns: `architecture.md` §2.8.
- **§1.7** `AddDbConfig` builds its default encryptor from `DataProtectionProvider.Create("DbConfig")` (`HostApplicationBuilderExtensions.cs:121`) — an instance independent of any host-level `AddDataProtection()` registration, whose key persistence is platform-dependent rather than guaranteed. Do not rely on it surviving a restart or being shared across instances: register a custom `IConfigEncryptor` (or an explicitly configured provider) before `AddDbConfig` for multi-instance or restart-stable deployments, or secrets become undecryptable — `project-specific.md` §8.9.
- **§1.8** The UI never reaches the database directly (AGENTS.md §0.3) — that boundary is what keeps the store a server-only surface.

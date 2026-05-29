# Security (§1)

> Cite rules as §1.N in reviews. Pairs with `.claude/references/security-checklist.md`.

## Secrets & Encryption

- **§1.1** `[ENFORCED]` Secret values (`IsSecret=true`) MUST be protected by a real `IConfigEncryptor`. The EF/provider default is `DataProtectionConfigEncryptor` (ASP.NET Data Protection). The in-memory default `PassthroughConfigEncryptor` is a no-op and stores plaintext — only acceptable in tests. Evidence: `DataProtectionConfigEncryptor.cs`, `PassthroughConfigEncryptor.cs`.
- **§1.2** `[CONVENTION]` Host-supplied encryptors win via `TryAddSingleton<IConfigEncryptor>` — register before `AddDbConfig`. Do not assume the default is secure.
- **§1.3** `[CONVENTION]` Reads can be audited (`DbConfigOptions.AuditReads`); secret values surface through `SecretDecryptionView` rather than raw decryption everywhere. Don't bypass it.

## UI / Admin API authorization

- **§1.4** `[AMBIGUOUS]` The admin UI is NOT secure-by-default: the two-arg `MapDbConfigUi` overload is intentionally fully open. WHEN wiring the UI for any non-local deployment, supply an authorization filter (`LocalRequestsOnlyAuthorizationFilter`, `CookieAuthorizationFilter`, or `.RequireAuthorization`). Evidence: `OpenAccessByDefaultTests.cs`, `EndpointRouteBuilderExtensions.cs:79-103`.
- **§1.5** `[CONVENTION]` Built-in login requires a host-registered `IDbConfigCredentialValidator`; it throws if missing. Never ship a stub validator that returns success.

## General

- **§1.6** NEVER log raw secret values or credentials. Audit rows record metadata (action, key, who) — not decrypted secret payloads.
- **§1.7** NEVER hardcode connection strings or secrets in source. They flow through provider options / host configuration.

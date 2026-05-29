# Architecture (§2)

> Cite rules as §2.N. Full detail in `.claude/references/architecture-principles.md`.

## Layering

- **§2.1** `[ENFORCED]` Abstractions (`I{Capability}` interfaces) live in `DbConfig.Core`. Concrete implementations (EF, providers, HTTP, UI) depend on Core; Core depends on nothing in this solution. Do not add a Core → impl reference.
- **§2.2** `[CONVENTION]` Package-per-concern: persistence → `DbConfig.EntityFrameworkCore` / provider packages; HTTP → `DbConfig.Http`; admin UI/auth → `DbConfig.Ui`. Put new code in the matching package.
- **§2.3** `[CONVENTION]` The store contract is ISP-split (`IConfigReader`, `IConfigWriter`, `IConfigSnapshotReader`, `IConfigPollingStore`, `IConfigAuditStore`, `IConfigAuditAppender`). Depend on the narrowest interface you need; don't re-fatten the contract.

## Patterns

- **§2.4** `[CONVENTION]` Registration is the fluent `DbConfigBuilder` over `IServiceCollection`; providers extend it (`UseEntityFrameworkCore`/`UsePostgreSql`/`UseSqlServer`). New opt-in behavior should hang off the builder, not a new top-level `Add*` call.
- **§2.5** `[CONVENTION]` Use `TryAdd*` for default registrations so host overrides win (encryptor, tenant resolver, credential validator).
- **§2.6** `[ENFORCED]` HTTP is minimal-API: `internal static {Verb}{Entity}Endpoint` classes with `HandleAsync` + `[FromServices]` injection. No MVC controllers exist (`find . -name "*Controller*.cs"` → 0) — do not add them.
- **§2.7** `[CONVENTION]` No mediator/CQRS layer — endpoints call store interfaces directly. Don't introduce MediatR.

## Cross-cutting

- **§2.8** `[CONVENTION]` Inject `TimeProvider` for clocks (never `DateTime.UtcNow` directly in new code where testability matters). Use `ILogger<TMarker>` with a dedicated marker type for category names.
- **§2.9** `[CONVENTION]` Observability side-effects (read audit) degrade gracefully: warn-once + fire-and-forget, never throw on misconfiguration.

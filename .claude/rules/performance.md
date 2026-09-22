---
paths:
  - "src/core/DbConfig.Core/**"
  - "src/core/DbConfig.EntityFrameworkCore/**"
axes:
  decision: structure
  topic: performance
  scope: project
---

# Performance

> Scope: the polling loop and the `IConfiguration` read path — the only two hot paths in this package.

- **§6.1** The polling loop is the hot path. `GetLatestModifiedUtcScopedAcrossAllTenantsAsync` (`DbConfigConfigurationProvider.cs:262`) is the ONLY query it runs when nothing has changed, and it must stay a cheap `MAX(ModifiedUtc)` over the `(Scope, Environment, TenantId, ModifiedUtc)` index. Never add per-tick work beyond: read watermark, compare, reload-if-advanced.
- **§6.2** `TryGet` runs on every `IConfiguration[key]` read. It does three things and no more: resolve the tenant (an `AsyncLocal` override takes precedence over `ITenantResolver.Resolve()`), look up the in-memory dictionaries, and — for entries flagged secret — call `SecretDecryptionView.Decrypt` (`DbConfigConfigurationProvider.cs:132,140`). Never add I/O, DB calls, or locking. Resolvers MUST be cheap for the same reason — `project-specific.md` §8.14.
- **§6.3** Signal-driven wake-up over shorter poll intervals. The HTTP write endpoints call `IDbConfigReloadSignal.TriggerReload()` so in-process consumers see changes immediately. **Don't reduce `ReloadInterval` as a performance hack** — fire the signal instead.
- **§6.4** Select only the columns you need; use `.Select()` projections for read paths (`data-layer.md` §5.3).
- **§6.5** Read auditing trades zero-loss for latency deliberately: audit rows for GETs are fire-and-forget so a GET never acquires a write transaction. Do not "fix" this into an in-transaction write — see `architecture.md` §2.14. The inverse is also load-bearing: mutation audits stay in-transaction (AGENTS.md §0.5).
- **§6.6** Memory ceiling for multi-tenant hosts: all tenants load eagerly on every reload. Recommended ceiling ~10K tenants × 100 keys (~200 MB). Beyond that, lazy per-tenant loading is the tracked fix — do not silently exceed it.
- **§6.7** No premature optimization. Measure before optimizing; this repo ships no benchmark project, so a performance claim needs a reproducible measurement in the PR, not an assertion.

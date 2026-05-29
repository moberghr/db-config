# Git & Workflow (§8)

> Cite rules as §8.N.

- **§8.1** `[CONVENTION]` Commit messages use conventional prefixes: `feat:`, `fix:`, `refactor:`, `test:`, `chore:`, `docs:`, `polish:`. Release commits are `vX.Y.Z — <summary>`. Evidence: `git log` (e.g. `refactor: split IConfigStore along ISP lines`, `v0.14.0 — SOLID review pass`).
- **§8.2** `[CONVENTION]` Main branch is `main`; feature work branches off and merges back via PR (`#1`, `#2` in history).
- **§8.3** `[ENFORCED]` CI runs on GitHub Actions (`.github/workflows/test.yml`, `release.yml`, `deploy-docs.yml`). Builds use `TreatWarningsAsErrors=true` — a warning fails CI. Don't push code that warns.
- **§8.4** `[CONVENTION]` This is a published NuGet library — treat public API changes as breaking and bump the version (`src/core/` projects pack with symbols + source link). Keep `net8.0` as the single target framework.

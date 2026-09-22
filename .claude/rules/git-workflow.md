---
paths:
  - "**"   # repo-wide: branch/commit/PR conventions apply to every change
axes:
  decision: process
  topic: git
  scope: project
---

# Git & Workflow

- **§7.1** Branch naming: hierarchical with `/`. Actual branches in this repo: `feat/postgresql-data-source`, `fix/nuget-audit-advisories`, `feat/mtk-setup-v7.10.1`. Prefixes in use: `feat/`, `fix/`, `chore/`, `docs/`, `test/`, `bug/`.
- **§7.2** Commit messages: imperative mood, describe the "what" concisely. Examples from recent history:
  - `feat(postgresql): accept an NpgsqlDataSource in UsePostgreSql and the migrator`
  - `fix: clear NuGet audit advisories blocking release and test CI`
  - `refactor: split IConfigStore along ISP lines`
  - `test: hoist schema name to const and seed via EF in tenant schema tests`
  - Release commits use a version header instead: `v0.15.0 — multi-target packages and tests to net8.0;net10.0`
- **§7.3** Static analyzers are enforced in build: StyleCop, Roslynator, SonarAnalyzer, Meziantou. All registered in `src/Directory.Build.props`. `TreatWarningsAsErrors=true` — **the build must pass with zero warnings**.
- **§7.4** `.editorconfig` enforces code style at the IDE level. Do not override severity levels in individual projects without a justified reason.
- **§7.5** **NEVER push to the remote without explicit approval**, even when CI is green. The engineer reviews the diff first. PRs are merged via GitHub UI after review.
- **§7.6** Never use `--no-verify` to skip pre-commit hooks. Never use `--force` to push to `main`. Investigate hook failures — fix the underlying issue.

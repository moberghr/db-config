# DbConfig.Ui — Local Context

> This package lives in a monorepo. See root `AGENTS.md` for team-wide standards.
> This file only documents what's specific to this package.

## What this is

Serves the embedded React admin app: `MapDbConfigUi`, the unified `MapDbConfigAdmin` mount,
`EmbeddedStaticFileMiddleware`, and the built-in cookie login.

## Framework / runtime

C# host for a TypeScript SPA. The React source lives in `ui/` at the repo root (stack `typescript`
per `.claude/tech-stack.map`), not in this directory.

## Build / test (local)

The `BuildUI` MSBuild target runs `npm run build` in `ui/` during `dotnet build`, then the bundle is
embedded as resources. For UI iteration, run the Vite dev server instead of rebuilding the package:

```bash
cd ui && npm install && npm run dev
```

## Local conventions

- **The React app never reaches the database** (root `AGENTS.md` §0.3) — all traffic goes through
  `DbConfig.Http`'s JSON endpoints.
- The API prefix is injected at serve time via a `<meta name="db-config-api-prefix">` tag (`EmbeddedStaticFileMiddleware.cs:59`, read by `ui/src/api/client.ts:34`), so it is never
  hard-coded into the built bundle.
- `MapDbConfigAdmin` (in `MapDbConfigAdminExtensions.cs`) sets the cookie `Path` to its own prefix so
  one cookie covers UI and API.

## Dependencies / boundaries

- References `DbConfig.Core` + `DbConfig.Http`.

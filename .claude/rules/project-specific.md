# Project-Specific (§9)

> Cite rules as §9.N. Patterns unique to DbConfig.

- **§9.1** `[CONVENTION]` DbConfig surfaces DB-backed config as a standard .NET `IConfiguration` provider (`DbConfigConfigurationSource` → `DbConfigConfigurationProvider`). New config-surfacing features should integrate through this provider model, not a parallel read API.
- **§9.2** `[CONVENTION]` Reload/refresh is poll/signal based (`IConfigPollingStore`, `IDbConfigReloadSignal`, `IConfigWatermark`, `ReloadEndpoint`). There is no message bus — don't add one for change propagation.
- **§9.3** `[CONVENTION]` HTTP responses serialize via the shared `JsonOptions.Default` and signal not-found by setting `Response.StatusCode` directly; there is no `Result<T>`/`ProblemDetails` envelope. Match this in new endpoints.
- **§9.4** `[CONVENTION]` Keys are normalized (`/` → `:`) at the HTTP boundary before hitting the store. Preserve this normalization for any new key-addressed endpoint.
- **§9.5** `[CONVENTION]` Provider packages (`DbConfig.Provider.PostgreSql`, `DbConfig.Provider.SqlServer`) are thin: connection/casing pipeline + a static `Sql/InitialCreate.sql` (under each provider's `src/core/providers/.../Sql/` folder). Keep persistence logic shared in `DbConfig.EntityFrameworkCore`, not duplicated per provider.
- **§9.6** `[CONVENTION]` `ui/` (Vite/React admin SPA) and `website/` (Docusaurus docs) are subordinate to the .NET library and have their own toolchains; the primary stack is dotnet. Don't treat them as the main app.

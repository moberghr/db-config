---
sidebar_position: 3
---

# Reload signal

The reload signal lets the HTTP layer notify the polling provider to reload immediately
without waiting for the next poll interval. It bridges the two in-process components that
share the same database but no shared memory.

## `IDbConfigReloadSignal.Trigger()`

The interface has a single method:

```csharp
public interface IDbConfigReloadSignal
{
    void Trigger();
}
```

Calling `Trigger()` causes the `DbConfigConfigurationProvider` to re-read from the store
immediately and fire `IChangeToken` if any values changed. This is the same thing the
polling timer does on every tick, but on-demand.

## Who calls it

The HTTP layer calls `Trigger()` automatically after every mutation:

- `PUT /{scope}/{environment}/{*key}` — calls `Trigger()` after `UpsertAsync`
- `DELETE /{scope}/{environment}/{*key}` — calls `Trigger()` after `DeleteAsync`
- `POST /reload` — calls `Trigger()` directly and returns 204

This means in-process consumers see the updated values within milliseconds of a write via
the API or UI.

## Manual trigger via HTTP

You can trigger an immediate reload from outside the process using the HTTP endpoint:

```bash
curl -X POST http://localhost:5000/api/dbconfig/reload \
  -H "Authorization: Bearer <token>"
```

This is useful after an out-of-band database change (e.g. a migration script that inserts
seed data). However, note that the watermark-based polling loop will also pick up the change
within one `ReloadInterval` (default 30 seconds) even without the explicit signal.

## Lazy resolution

`IDbConfigReloadSignal` is registered as a lazy factory in DI. The concrete implementation
(`DbConfigConfigurationProvider`) is not available until after `WebApplication.Build()` and
the configuration source's `Load()` have been called.

Resolving `IDbConfigReloadSignal` during `ConfigureServices` or in the body of extension
methods (before `Build()` returns) will throw `InvalidOperationException` with a clear
message.

The correct pattern — resolve inside a request handler or hosted service:

```csharp
// Correct: resolved post-Build inside a handler
app.MapPost("/admin/config/reload", (IDbConfigReloadSignal signal) =>
{
    signal.Trigger();
    return Results.NoContent();
});

// Correct: resolved in a hosted service
public class MyService(IDbConfigReloadSignal signal) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        signal.Trigger(); // safe — hosted services run post-Build
        await Task.Delay(Timeout.Infinite, ct);
    }
}

// Wrong: resolved during ConfigureServices — throws
builder.Services.AddSingleton<IFoo>(sp =>
{
    var signal = sp.GetRequiredService<IDbConfigReloadSignal>(); // InvalidOperationException
    return new Foo(signal);
});
```

## Interaction with `IChangeToken` and `IOptionsMonitor<T>`

The reload cycle:

1. `Trigger()` is called
2. The polling provider's timer callback wakes up (or an internal set-event mechanism fires)
3. `GetAllAsync` / `GetAllScopedAsync` is called on the store
4. If any values changed, the provider updates its internal dictionary and calls `OnReload()`
5. `OnReload()` fires the `IChangeToken`, which notifies all registered change-token consumers
6. `IOptionsMonitor<T>.OnChange` callbacks receive the new values

From step 1 to step 6, the typical latency is under 100ms on a local database. On a
remote database with network round-trips, it is bounded by the store round-trip time.

## Polling fallback

Even without an explicit `Trigger()` call, the polling timer runs every `ReloadInterval`
(default 30 seconds). The signal is an optimization for low-latency scenarios; the polling
loop is the safety net.

The two paths are complementary. Direct SQL mutations (bypassing the API) are only visible
via the polling path, and only when another row's `ModifiedUtc` advances. See the
[Introduction](../intro.md) for the full list of known limitations.

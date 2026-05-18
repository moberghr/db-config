using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using DbConfig.Core;
using DbConfig.Http;
using DbConfig.Tests.TestData;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;

namespace DbConfig.Tests.Http;

/// <summary>
/// Unit tests for the opt-in read-audit feature (<see cref="DbConfigOptions.AuditReads"/>).
/// All tests use TestServer, InMemoryConfigStore, and InMemoryConfigAuditStore.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ReadAuditEndpointTests
{
    private const string App = "ReadAuditApp";
    private const string Env = "Test";

    private static async Task WaitForAuditRowAsync(
        InMemoryConfigAuditStore auditStore,
        Func<ConfigAuditEntry, bool> predicate,
        CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (auditStore.AllEntries.Any(predicate))
            {
                return;
            }

            await Task.Delay(10, ct);
        }
    }

    [TimedFact]
    public async Task AuditReads_OffByDefault_NoRowsWritten()
    {
        var auditStore = new InMemoryConfigAuditStore();
        var store = new InMemoryConfigStore();
        var now = DateTimeOffset.UtcNow;
        await store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "SomeKey", "val", false, now, null), CancellationToken.None);

        var options = new DbConfigOptions { AuditReads = false };

        await using var app = BuildApp(store, auditStore, options);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        await client.GetAsync($"/api/dbconfig/{App}/{Env}/SomeKey", TestContext.Current.CancellationToken);
        await client.GetAsync($"/api/dbconfig/?appName={App}&environment={Env}", TestContext.Current.CancellationToken);

        await Task.Delay(100, TestContext.Current.CancellationToken);

        auditStore.AllEntries.ShouldBeEmpty();
    }

    [TimedFact]
    public async Task AuditReads_On_GetSingle_WritesReadAuditRow()
    {
        var auditStore = new InMemoryConfigAuditStore();
        var store = new InMemoryConfigStore();
        var now = DateTimeOffset.UtcNow;
        await store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "MyKey", "myval", false, now, null), CancellationToken.None);

        var options = new DbConfigOptions { AuditReads = true };

        await using var app = BuildApp(store, auditStore, options);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync($"/api/dbconfig/{App}/{Env}/MyKey", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        await WaitForAuditRowAsync(
            auditStore,
            e => e.Action == ConfigAuditAction.Read && string.Equals(e.Key, "MyKey", StringComparison.Ordinal),
            TestContext.Current.CancellationToken);

        var readRows = auditStore.AllEntries.Where(e => e.Action == ConfigAuditAction.Read).ToList();
        readRows.Count.ShouldBe(1);

        var row = readRows[0];
        row.AppName.ShouldBe(App);
        row.Environment.ShouldBe(Env);
        row.Key.ShouldBe("MyKey");
        row.OldValue.ShouldBeNull();
        row.NewValue.ShouldBeNull();
        row.IsSecret.ShouldBeFalse();
    }

    [TimedFact]
    public async Task AuditReads_On_GetSingleNotFound_StillWritesReadAuditRow()
    {
        var auditStore = new InMemoryConfigAuditStore();
        var store = new InMemoryConfigStore();
        var options = new DbConfigOptions { AuditReads = true };

        await using var app = BuildApp(store, auditStore, options);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync($"/api/dbconfig/{App}/{Env}/NonExistentKey", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await WaitForAuditRowAsync(
            auditStore,
            e => e.Action == ConfigAuditAction.Read && string.Equals(e.Key, "NonExistentKey", StringComparison.Ordinal),
            TestContext.Current.CancellationToken);

        var readRows = auditStore.AllEntries.Where(e => e.Action == ConfigAuditAction.Read).ToList();
        readRows.Count.ShouldBe(1);
        readRows[0].Key.ShouldBe("NonExistentKey");
    }

    [TimedFact]
    public async Task AuditReads_On_GetList_WritesReadAuditRowWithKeyStar()
    {
        var auditStore = new InMemoryConfigAuditStore();
        var store = new InMemoryConfigStore();
        var options = new DbConfigOptions { AuditReads = true };

        await using var app = BuildApp(store, auditStore, options);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            $"/api/dbconfig/?appName={App}&environment={Env}",
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        await WaitForAuditRowAsync(
            auditStore,
            e => e.Action == ConfigAuditAction.Read && string.Equals(e.Key, "*", StringComparison.Ordinal),
            TestContext.Current.CancellationToken);

        var readRows = auditStore.AllEntries.Where(e => e.Action == ConfigAuditAction.Read).ToList();
        readRows.Count.ShouldBe(1);

        var row = readRows[0];
        row.Key.ShouldBe("*");
        row.AppName.ShouldBe(App);
        row.Environment.ShouldBe(Env);
        row.OldValue.ShouldBeNull();
        row.NewValue.ShouldBeNull();
    }

    [TimedFact]
    public async Task AuditReads_On_HistoryEndpoint_DoesNotRecurse()
    {
        var auditStore = new InMemoryConfigAuditStore();
        var store = new InMemoryConfigStore();
        var options = new DbConfigOptions { AuditReads = true };

        await using var app = BuildApp(store, auditStore, options);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            $"/api/dbconfig/{App}/{Env}/audit/SomeKey",
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        await Task.Delay(200, TestContext.Current.CancellationToken);

        var readRows = auditStore.AllEntries.Where(e => e.Action == ConfigAuditAction.Read).ToList();
        readRows.ShouldBeEmpty();
    }

    [TimedFact]
    public async Task AuditReads_On_AuditWriteFails_GetStillReturnsValue()
    {
        var store = new InMemoryConfigStore();
        var now = DateTimeOffset.UtcNow;
        await store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "KeyA", "value-a", false, now, null), CancellationToken.None);

        var options = new DbConfigOptions { AuditReads = true };
        var capturedWarnings = new List<string>();
        var throwingAuditStore = new ThrowingAuditStore();

        await using var app = BuildAppWithCustomAuditStore(store, throwingAuditStore, options, capturedWarnings);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync($"/api/dbconfig/{App}/{Env}/KeyA", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entry = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(
            TestContext.Current.CancellationToken);
        entry.GetProperty("value").GetString().ShouldBe("value-a");

        await Task.Delay(300, TestContext.Current.CancellationToken);

        capturedWarnings.Count.ShouldBeGreaterThan(0);
        capturedWarnings[0].ShouldContain("Read audit write failed");
    }

    [TimedFact]
    public async Task AuditReads_AsyncFaultedAuditWrite_StillReturnsValueWithLoggedWarning()
    {
        var store = new InMemoryConfigStore();
        var now = DateTimeOffset.UtcNow;
        await store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "KeyB", "value-b", false, now, null), CancellationToken.None);

        var options = new DbConfigOptions { AuditReads = true };
        var capturedWarnings = new List<string>();
        var asyncThrowingAuditStore = new AsyncThrowingAuditStore();

        await using var app = BuildAppWithCustomAuditStore(store, asyncThrowingAuditStore, options, capturedWarnings);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync($"/api/dbconfig/{App}/{Env}/KeyB", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entry = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(
            TestContext.Current.CancellationToken);
        entry.GetProperty("value").GetString().ShouldBe("value-b");

        // Wait for the ContinueWith(OnlyOnFaulted) callback to fire.
        await Task.Delay(300, TestContext.Current.CancellationToken);

        capturedWarnings.Count.ShouldBeGreaterThan(0);
        capturedWarnings[0].ShouldContain("Read audit write failed");
    }

    [TimedFact]
    public async Task AuditReads_On_NoAuditStoreRegistered_LogsWarningOnce()
    {
        var store = new InMemoryConfigStore();
        var now = DateTimeOffset.UtcNow;
        await store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "KeyC", "value-c", false, now, null), CancellationToken.None);

        var options = new DbConfigOptions { AuditReads = true };
        var capturedWarnings = new List<string>();

        await using var app = BuildAppWithNoAuditStore(store, options, capturedWarnings);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        // First GET — should log the warning once.
        var response = await client.GetAsync($"/api/dbconfig/{App}/{Env}/KeyC", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        await Task.Delay(100, TestContext.Current.CancellationToken);

        capturedWarnings.Count.ShouldBeGreaterThan(0);
        capturedWarnings.Any(w => w.Contains("AuditReads is true", StringComparison.Ordinal)).ShouldBeTrue();

        var warningCountAfterFirst = capturedWarnings.Count(w => w.Contains("AuditReads is true", StringComparison.Ordinal));

        // Second GET — the warning must NOT fire again (single-emission guard via Interlocked).
        var response2 = await client.GetAsync($"/api/dbconfig/{App}/{Env}/KeyC", TestContext.Current.CancellationToken);
        response2.StatusCode.ShouldBe(HttpStatusCode.OK);

        await Task.Delay(100, TestContext.Current.CancellationToken);

        var warningCountAfterSecond = capturedWarnings.Count(w => w.Contains("AuditReads is true", StringComparison.Ordinal));
        warningCountAfterSecond.ShouldBe(warningCountAfterFirst);
    }

    [TimedFact]
    public async Task AuditReads_On_ModifiedByCapturesAuthenticatedUserName()
    {
        var auditStore = new InMemoryConfigAuditStore();
        var store = new InMemoryConfigStore();
        var now = DateTimeOffset.UtcNow;
        await store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "AuthKey", "authval", false, now, null), CancellationToken.None);

        var options = new DbConfigOptions { AuditReads = true };

        await using var app = BuildAppWithAuth(store, auditStore, options);
        await app.StartAsync(TestContext.Current.CancellationToken);

        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "testuser");

        var response = await client.GetAsync($"/api/dbconfig/{App}/{Env}/AuthKey", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        await WaitForAuditRowAsync(
            auditStore,
            e => e.Action == ConfigAuditAction.Read && string.Equals(e.ModifiedBy, "testuser", StringComparison.Ordinal),
            TestContext.Current.CancellationToken);

        var readRows = auditStore.AllEntries.Where(e => e.Action == ConfigAuditAction.Read).ToList();
        readRows.Count.ShouldBe(1);
        readRows[0].ModifiedBy.ShouldBe("testuser");
    }

    private static WebApplication BuildApp(
        IConfigStore store,
        InMemoryConfigAuditStore auditStore,
        DbConfigOptions options)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IConfigStore>(store);
        builder.Services.AddSingleton<IConfigAuditStore>(auditStore);
        builder.Services.AddSingleton<IDbConfigReloadSignal, NoOpReloadSignal>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(options);

        var app = builder.Build();
        app.MapDbConfigHttp("/api/dbconfig");

        return app;
    }

    private static WebApplication BuildAppWithNoAuditStore(
        IConfigStore store,
        DbConfigOptions options,
        List<string> capturedWarnings)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IConfigStore>(store);

        // No IConfigAuditStore registered — intentionally omitted for this test.
        builder.Services.AddSingleton<IDbConfigReloadSignal, NoOpReloadSignal>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<ILoggerFactory>(new CapturingLoggerFactory(capturedWarnings));

        var app = builder.Build();
        app.MapDbConfigHttp("/api/dbconfig");

        return app;
    }

    private static WebApplication BuildAppWithCustomAuditStore(
        IConfigStore store,
        IConfigAuditStore auditStore,
        DbConfigOptions options,
        List<string> capturedWarnings)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IConfigStore>(store);
        builder.Services.AddSingleton<IConfigAuditStore>(auditStore);
        builder.Services.AddSingleton<IDbConfigReloadSignal, NoOpReloadSignal>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<ILoggerFactory>(new CapturingLoggerFactory(capturedWarnings));

        var app = builder.Build();
        app.MapDbConfigHttp("/api/dbconfig");

        return app;
    }

    private static WebApplication BuildAppWithAuth(
        IConfigStore store,
        InMemoryConfigAuditStore auditStore,
        DbConfigOptions options)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IConfigStore>(store);
        builder.Services.AddSingleton<IConfigAuditStore>(auditStore);
        builder.Services.AddSingleton<IDbConfigReloadSignal, NoOpReloadSignal>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(options);
        builder.Services.AddAuthentication("FakeUser")
            .AddScheme<AuthenticationSchemeOptions, NamedUserAuthHandler>("FakeUser", _ => { });

        var app = builder.Build();
        app.UseAuthentication();
        app.MapDbConfigHttp("/api/dbconfig");

        return app;
    }

    private sealed class NoOpReloadSignal : IDbConfigReloadSignal
    {
        public void Trigger()
        {
        }
    }

    private sealed class ThrowingAuditStore : IConfigAuditStore
    {
        public Task<IReadOnlyList<ConfigAuditEntry>> GetHistoryAsync(
            string appName, string environment, string key, int take, CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyList<ConfigAuditEntry>>([]);
        }

        public Task<IReadOnlyList<ConfigAuditEntry>> GetHistoryForTenantAsync(
            string appName, string environment, string tenantId, string key, int take, CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyList<ConfigAuditEntry>>([]);
        }

        public Task WriteAsync(ConfigAuditEntry entry, CancellationToken ct)
        {
            throw new InvalidOperationException("Simulated audit store failure.");
        }
    }

    /// <summary>
    /// Unlike <see cref="ThrowingAuditStore"/> (which throws synchronously), this store returns
    /// a faulted task — exercising the ContinueWith(OnlyOnFaulted) branch in WriteReadAudit.
    /// </summary>
    private sealed class AsyncThrowingAuditStore : IConfigAuditStore
    {
        public Task<IReadOnlyList<ConfigAuditEntry>> GetHistoryAsync(
            string appName, string environment, string key, int take, CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyList<ConfigAuditEntry>>([]);
        }

        public Task<IReadOnlyList<ConfigAuditEntry>> GetHistoryForTenantAsync(
            string appName, string environment, string tenantId, string key, int take, CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyList<ConfigAuditEntry>>([]);
        }

        public Task WriteAsync(ConfigAuditEntry entry, CancellationToken ct)
        {
            return Task.FromException(new InvalidOperationException("Simulated async audit store fault."));
        }
    }

    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
        private readonly List<string> _warnings;

        public CapturingLoggerFactory(List<string> warnings)
        {
            _warnings = warnings;
        }

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new CapturingLogger(_warnings);
        }

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly List<string> _warnings;

        public CapturingLogger(List<string> warnings)
        {
            _warnings = warnings;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
            {
                lock (_warnings)
                {
                    _warnings.Add(formatter(state, exception));
                }
            }
        }
    }

    private sealed class NamedUserAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public NamedUserAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var userName) || string.IsNullOrEmpty(userName))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, userName!)],
                "FakeUser");
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, "FakeUser");

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}

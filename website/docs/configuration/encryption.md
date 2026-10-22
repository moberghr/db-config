---
sidebar_position: 3
---

# Encryption

DbConfig encrypts configuration values on a per-entry basis using the `IsSecret` flag.
Non-secret entries stay plaintext; secret entries are encrypted at rest.

## The `IsSecret` flag

Set `IsSecret = true` on any entry that contains sensitive data:

- Connection strings
- API keys and OAuth client secrets
- JWT signing keys
- Passwords and tokens

Leave `IsSecret = false` for non-sensitive configuration: feature flags, log levels, polling
intervals, public OAuth client IDs, URLs, and numeric tuning parameters. These stay plaintext
in the database, which makes operational debugging with SSMS or psql much easier.

When `IsSecret = true`:

- The React UI masks the value (`•••••`) and requires an explicit click to reveal it
- `EfCoreConfigStore.UpsertAsync` calls `IConfigEncryptor.Protect()` before writing
- `EfCoreConfigStore.GetAsync` / `GetAllAsync` call `IConfigEncryptor.Unprotect()` on read
- HTTP GET responses return plaintext — the store decrypts before returning. Write-only
  entries are the exception; see below.

## Write-only secrets (`IsWriteOnly`)

Mark an entry `IsWriteOnly = true` when nobody should be able to read it back through the API
or the UI, the way GitHub Actions secrets work. You can create the value and replace it, but
you cannot see it again. The host application still reads the plaintext through
`IConfiguration`, `IOptions<T>` and `IConfigStore`, exactly as it does for any other entry.

- **Always encrypted.** The store forces `IsSecret = true` for a write-only entry, so it is
  encrypted at rest like any other secret. `IsSecret` on its own stays revealable; the two flags
  are independent, and existing secrets are not affected.
- **Redacted by the HTTP API.** `GET /`, `GET /{scope}/{environment}/{*key}`, `GET /audit` and
  `GET /{scope}/{environment}/audit/{*key}` return `value` (or `oldValue` / `newValue`) as
  `null` and `isWriteOnly: true`. Redaction happens on the server, so the value never reaches
  the browser.
- **Replace, never reveal.** `PUT` always supplies the full value. Omitting `isWriteOnly`
  keeps the entry's current flag, so older clients and scripts cannot downgrade it by
  accident. Sending `isWriteOnly: false` explicitly clears the flag, but the request's value
  replaces the stored one, so the old value is never returned.
- **Audit rows.** An audit row is marked write-only when the entry was write-only before *or*
  after the change. The audit endpoints also hide the values of **every** row whose key is
  write-only right now, including rows written before it became write-only. The values are
  still stored (encrypted) in the audit table.
- **Rotate when you upgrade.** Making an existing entry write-only does not make its previous
  value secret again: anyone who could read it before may have copied it. The UI asks for a
  new value when you tick write-only on an existing entry.

```csharp
await store.UpsertAsync(
    new ConfigEntryRecord("MyApp", "Production", "", "Jwt:SigningKey", signingKey,
        IsSecret: true, DateTimeOffset.UtcNow, "deploy-bot", IsWriteOnly: true),
    ct);
```

:::warning Write-only is not an authorization boundary
It hides the value from people using the HTTP API and the UI. It does **not** hide it from:

- any application whose host includes the scope (`Scope` or `IncludeScopes`), which reads the
  plaintext in-process;
- in-process code calling `IConfigStore` / `IConfigAuditStore` directly;
- anyone with database access **and** the Data Protection key ring.

Control who can write and who runs the reading applications with the same care as for any
other secret. See [shared scopes](./scopes.md).
:::

A custom `IConfigStore` must persist `IsWriteOnly` and force `IsSecret = true` for it. The
upsert endpoint reads a write-only entry back after writing it and returns `500` (and logs an
error) if the store dropped the flag. A custom `IConfigAuditStore` must set
`ConfigAuditEntryRecord.IsWriteOnly` for rows written while the entry was write-only **and** for
rows whose key is write-only at read time; the shipped stores do both.

## Default encryptor: ASP.NET Core Data Protection

The default `IConfigEncryptor` implementation is `DataProtectionConfigEncryptor`, which
wraps `IDataProtectionProvider` from `Microsoft.AspNetCore.DataProtection`. It is
registered automatically by `AddDbConfig` via `TryAddSingleton`.

:::warning
The default Data Protection key ring is **ephemeral**. Keys are process-scoped and
regenerated on every restart. Any value encrypted with an ephemeral key ring becomes
permanently unreadable after the process exits.

Configure key persistence **before** calling `AddDbConfig` for any non-toy deployment.
:::

## Configure persistent key storage

Persistent keys survive process restarts and allow multiple instances to share the same
key ring. Configure key persistence before `AddDbConfig`:

```csharp
// Single-instance: persist to the local filesystem
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo("/var/dbconfig/keys"))
    .ProtectKeysWithCertificate("your-cert-thumbprint");

// Multi-instance on Azure: persist to Azure Blob Storage
builder.Services.AddDataProtection()
    .PersistKeysToAzureBlobStorage(blobClient)
    .ProtectKeysWithAzureKeyVault(keyIdentifier, tokenCredential);

// Multi-instance on AWS: persist to Systems Manager Parameter Store
builder.Services.AddDataProtection()
    .PersistKeysToAWSSystemsManager("/MyApp/DataProtectionKeys");

// Always register AddDbConfig AFTER configuring Data Protection
builder.AddDbConfig(b =>
{
    b.Options.Scope = "MyApp";
    b.Options.Environment = builder.Environment.EnvironmentName;
    b.UseSqlServer(connectionString);
});
```

`AddDbConfig` uses `TryAddSingleton` for `IDataProtectionProvider`, so any prior
configuration (your `AddDataProtection()` call above) takes precedence.

See [Key persistence](../operations/key-persistence.md) for cloud-specific examples and
key rotation details.

## Custom `IConfigEncryptor`

For integrations with Azure Key Vault, AWS KMS, HashiCorp Vault, or other key management
systems, implement `IConfigEncryptor` and register it before `AddDbConfig`:

```csharp
public sealed class AzureKeyVaultEncryptor : IConfigEncryptor
{
    private readonly KeyClient _client;

    public AzureKeyVaultEncryptor(KeyClient client) => _client = client;

    public string Protect(string plaintext)
    {
        // encrypt with KMS; return Base64-encoded ciphertext
    }

    public string Unprotect(string ciphertext)
    {
        // decrypt with KMS; return plaintext
    }
}
```

### Instance-registered

Register an already-constructed instance. Works immediately on first `Load()`:

```csharp
var client = new KeyClient(new Uri(vaultUri), new DefaultAzureCredential());
builder.Services.AddSingleton<IConfigEncryptor>(new AzureKeyVaultEncryptor(client));
builder.AddDbConfig(b => { ... });
```

### Type-mapped

Let DI resolve the encryptor and its dependencies after the host is built:

```csharp
// DI resolves KeyClient, IOptions<KmsOptions>, ILogger<...> etc.
builder.Services.AddSingleton<IConfigEncryptor, AzureKeyVaultEncryptor>();
builder.AddDbConfig(b => { ... });
```

:::warning
With type-mapped registration, decryption is deferred until after `host.StartAsync()` runs
the `DbConfigEncryptorActivator` hosted service. Reading a secret config value
**before `host.StartAsync()`** (e.g. inside `ConfigureServices` or another extension's
bootstrap) throws `InvalidOperationException` with this message:

> Cannot read secret value 'X' before host.Build() has returned. Move this read to a
> request handler, hosted service, or OnStarted callback.

Non-secret config reads are unaffected. Request handlers, background services, and
`IOptions<T>` consumers (all post-build) work without any change.
:::

## Audit log and encryption

Audit rows store `OldValue` and `NewValue` in the same form as the main `Value` column —
ciphertext when `IsSecret = true`. The audit read endpoint (`GET .../audit/{key}`) decrypts
values before returning them to callers. You never see raw ciphertext from the HTTP API.
Rows for write-only entries come back with both values set to `null`.

## Flipping `IsSecret` post-hoc

Changing the `IsSecret` flag on an existing row after it has been stored is an unsupported
edge case:

- **`true` → `false`**: the stored value is ciphertext, but the store will no longer
  decrypt it (it only decrypts when `IsSecret = true`). The raw ciphertext leaks into
  `IConfiguration`.
- **`false` → `true`**: the stored value is plaintext. The store will call `Unprotect()` on
  it, which throws because it is not a valid Data Protection payload.

If you need to change the `IsSecret` flag on an entry, delete and re-create it with the
correct flag. This is intentional — silent re-encryption would mean data written during a
key-rotation window would be double-encrypted.

## Key rotation

ASP.NET Core Data Protection rotates keys automatically every 90 days. Old keys are
retained for decryption indefinitely unless you explicitly revoke them. You do not need to
do anything special for key rotation — the Data Protection stack handles it transparently.

Custom `IConfigEncryptor` implementations are responsible for their own key rotation
semantics.

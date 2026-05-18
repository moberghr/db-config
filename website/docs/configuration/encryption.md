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
- HTTP GET responses always return plaintext — the store decrypts before returning

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
    b.Options.AppName = "MyApp";
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

### Instance-registered (v0.5.0+)

Register an already-constructed instance. Works immediately on first `Load()`:

```csharp
var client = new KeyClient(new Uri(vaultUri), new DefaultAzureCredential());
builder.Services.AddSingleton<IConfigEncryptor>(new AzureKeyVaultEncryptor(client));
builder.AddDbConfig(b => { ... });
```

### Type-mapped (v0.6.0+)

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

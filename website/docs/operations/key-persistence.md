---
sidebar_position: 2
---

# Key persistence

DbConfig encrypts secret entries using ASP.NET Core Data Protection. The default key ring
is **ephemeral** — keys are process-scoped and not persisted. Encrypted values stored with
an ephemeral key ring become permanently unreadable after the process exits.

For any deployment where you use `IsSecret = true` and need values to survive a restart
(which is almost every production deployment), configure key persistence before calling
`AddDbConfig`.

## Why this matters

Without persistent keys:
- Values encrypted in one deployment are unreadable after a redeploy
- Multiple instances of the same app cannot decrypt each other's values
- Rolling restarts (Kubernetes, App Service, etc.) cause decryption failures

With persistent keys, the key ring is shared across instances and restarts. Data Protection
handles key rotation automatically every 90 days while retaining old keys for decryption.

## Single-instance: filesystem

For a single-instance host (a VM, a container with a persistent volume mount):

```csharp
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo("/var/dbconfig/keys"))
    .ProtectKeysWithCertificate("your-cert-thumbprint");

builder.AddDbConfig(b => { ... });
```

The directory must be writable by the process. If `ProtectKeysWithCertificate` is omitted,
keys are stored unencrypted on disk — suitable only for environments where the filesystem
is already protected.

## Multi-instance: Azure Blob Storage

For Azure App Service, Azure Container Apps, or any multi-instance deployment on Azure:

```bash
dotnet add package Microsoft.AspNetCore.DataProtection.AzureStorage
dotnet add package Microsoft.AspNetCore.DataProtection.AzureKeyVault
```

```csharp
var blobClient = new BlobClient(
    new Uri("https://myaccount.blob.core.windows.net/keys/dataprotection.xml"),
    new DefaultAzureCredential());

var keyIdentifier = new Uri("https://myvault.vault.azure.net/keys/my-key");

builder.Services.AddDataProtection()
    .PersistKeysToAzureBlobStorage(blobClient)
    .ProtectKeysWithAzureKeyVault(keyIdentifier, new DefaultAzureCredential());

builder.AddDbConfig(b => { ... });
```

## Multi-instance: AWS Systems Manager

For ECS, Fargate, or EC2 deployments on AWS:

```bash
dotnet add package Amazon.AspNetCore.DataProtection.SSM
```

```csharp
builder.Services.AddDataProtection()
    .PersistKeysToAWSSystemsManager("/MyApp/DataProtectionKeys");

builder.AddDbConfig(b => { ... });
```

The IAM role of the service must have `ssm:GetParametersByPath`, `ssm:PutParameter`,
and `ssm:DeleteParameter` permissions on the configured path.

## Multi-instance: Redis

For self-hosted Redis:

```bash
dotnet add package Microsoft.AspNetCore.DataProtection.StackExchangeRedis
```

```csharp
var redis = ConnectionMultiplexer.Connect("localhost");

builder.Services.AddDataProtection()
    .PersistKeysToStackExchangeRedis(redis, "DataProtection-Keys");

builder.AddDbConfig(b => { ... });
```

## Key rotation

ASP.NET Core Data Protection rotates keys automatically every 90 days. When a new key is
generated:
- Future encryptions use the new key
- Old keys are retained for decryption (indefinitely, unless explicitly revoked)
- Existing encrypted values continue to work without any manual migration

Key rotation is transparent to DbConfig. You do not need to re-encrypt existing entries
when a new key is generated.

## Verifying persistence is working

After configuring persistence, verify by:

1. Writing a secret entry via the UI or HTTP API
2. Restarting the application
3. Reading the entry back — it should return the original plaintext value

If decryption fails after restart with an error like "The key &#123;guid&#125; was not found in the
key ring", the keys are not persisting correctly. Check directory/blob/SSM permissions.

## Custom `IConfigEncryptor`

If you use a fully custom `IConfigEncryptor` (Azure Key Vault native, AWS KMS, etc.), the
Data Protection key ring is bypassed entirely. Key lifecycle management is then the
responsibility of your encryptor implementation.

See [Encryption](../configuration/encryption.md) for custom encryptor registration.

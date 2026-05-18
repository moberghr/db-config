# Encryption layering audit (v1.4)

**FINDING:** Real bug. With instance-registered or default `IConfigEncryptor`, the polling
provider's `_encryptor` field stays `null` forever, so the first `IConfiguration[key]`
read of any `IsSecret=true` entry throws `InvalidOperationException` ("Cannot read secret
config value '...' before host.Build() has returned"). The bug is masked by lenient test
encryptors that silently no-op `Unprotect` when ciphertext doesn't carry their tag.

**EVIDENCE:**

1. `HostApplicationBuilderExtensions.cs:88-113` — when consumer registers an instance
   (`ImplementationInstance is IConfigEncryptor`) or registers nothing, `needsActivator`
   is set to `false` and `DbConfigEncryptorActivator` is never added. The activator is
   the only place `provider.SetEncryptor(...)` is called (`DbConfigEncryptorActivator.cs:46-53`).
2. `EfCoreConfigStore.cs:488-513` (`GetAllScopedForAllTenantsAsync`) calls
   `DecryptEntry` for every row. So the polling store, given a real encryptor, returns
   **plaintext** to the provider's `LoadAsync` (`DbConfigConfigurationProvider.cs:283-318`).
   `_tenantData` holds plaintext, but `_isSecretByTenantKey` still says `true`.
3. `DbConfigConfigurationProvider.cs:180-200` (`DecryptIfSecret`) — for any IsSecret key,
   if `_encryptor is null` it throws at line 191-197. Otherwise it calls `Unprotect` on
   the value sitting in `_tenantData` — which is already plaintext in the instance/default
   path. So a "fix" by always wiring the encryptor would actually trigger a double-decrypt.
4. Existing tests do NOT cover the failing path. `EncryptorSharingTests` exercises
   `InMemoryConfigStore` directly (no provider involvement). `TypeMappedEncryptorTests`
   covers only the type-mapped path. `CompositionGapsTests.IsSecretTenantKey_RoundTripsThroughResolutionChain`
   uses an `InMemoryConfigStore` with a real encryptor AND calls `provider.SetEncryptor`
   manually AND its `TestEncryptor.Unprotect` is lenient (`startsWith("ENC:")` else
   return verbatim) — so even though the value in `_tenantData` is already plaintext,
   `Unprotect("secret-acme")` returns `"secret-acme"` and the test passes. The bug is
   hidden by stub leniency. There is no E2E test that calls `IConfiguration["Secret:Key"]`
   on a host built via `builder.AddDbConfig` with `IsSecret=true` data.

**IMPACT:** In production with the default DataProtection encryptor (the common path),
any consumer reading a secret via `IConfiguration`, `IOptionsSnapshot<T>`, or
`IOptionsMonitor<T>` throws on first access. This is a regression introduced when the
encryptor activator was added (v1.4) — before that, decryption was store-only and the
provider did not have a secret-check guard.

**FIX (Option B chosen — minimal):** In the instance-registered and default branches,
invoke `source.Provider?.SetEncryptor(pollingEncryptor)` **synchronously** after
`hostBuilder.Configuration.Add(source)` (which triggers `Build` and a synchronous
`Load`). At that point `source.Provider` is non-null and `Load` has populated
`_tenantData` with plaintext from the decrypting store.

To prevent double-decrypt, the store layering must change so the polling provider sees
the raw stored values (ciphertext for secrets) when the provider will be doing the
decryption. The simplest way: pass `null` as the polling-side encryptor (forcing
`PassthroughConfigEncryptor` in the store) in BOTH cases, then call `SetEncryptor` on
the provider with the real encryptor. The HTTP-side store still gets the real encryptor
via DI. This makes the polling pipeline uniform: store always returns raw, provider
always decrypts on `TryGet`. The type-mapped path is unchanged (provider gets the
encryptor later via the activator).

**Rejected options:** Option A regresses pre-build secret behavior for the instance
path. Option C requires the provider to know whether the store decrypted, which is
brittle. Option D is the cleanest end-state but requires test rewrites; Option B's
"store returns raw + provider always decrypts" achieves the same effect with one
local change in `AddDbConfig`.

**Tests touched:**
- `CompositionGapsTests.IsSecretTenantKey_RoundTripsThroughResolutionChain` — the
  `TestEncryptor.Unprotect` stub is strictened (throws on missing `ENC:` prefix).
  The test still passes after the fix because the provider now always decrypts
  ciphertext (store no longer decrypts on the polling load path when the polling
  encryptor is intentionally omitted).
- New test `InstanceRegisteredEncryptor_ProviderDecryptsSecretOnRead` in
  `TypeMappedEncryptorTests.cs` covers the instance-path provider decryption end
  to end via the new wiring.

Confidence: HIGH on the diagnosis. The fix is minimal and behind a single seam.

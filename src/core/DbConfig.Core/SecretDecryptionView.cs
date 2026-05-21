using System.Collections.Concurrent;

namespace DbConfig.Core;

/// <summary>
/// Holds the per-tenant secret flags loaded from the store and the
/// <see cref="IConfigEncryptor"/> used to decrypt secret values on read. Extracted from
/// <see cref="DbConfigConfigurationProvider"/> so the decryption decision (encrypt yes/no,
/// pre-build secret read throws) is testable in isolation from the polling loop.
/// </summary>
/// <remarks>
/// Lifecycle inside the polling provider:
/// <list type="number">
///   <item>
///     Each <c>LoadAsync</c> tick rebuilds the per-tenant secret-flag dictionary from the
///     store result and calls <see cref="UpdateSecretFlags"/>. Both the new dictionary and
///     the writes onto it complete before the volatile publish.
///   </item>
///   <item>
///     <c>TryGet</c> calls <see cref="Decrypt"/> with the raw value pulled from the
///     per-tenant data dictionary. The view looks up the secret flag, decides whether the
///     value is ciphertext, and either decrypts via <see cref="IConfigEncryptor"/> or
///     returns the value verbatim.
///   </item>
///   <item>
///     <see cref="SetEncryptor"/> is called once by the host (either synchronously by
///     <c>AddDbConfig</c> for the instance-registered path, or asynchronously by
///     <c>DbConfigEncryptorActivator</c> for the type-mapped path). A second call with a
///     different instance throws; repeated calls with the same instance are no-ops.
///   </item>
/// </list>
/// Concurrency: the secret-flag dictionary is volatile-published. The encryptor field is
/// volatile. Pre-build reads of secret values surface <see cref="InvalidOperationException"/>
/// with a clear hint to defer the read until after <c>host.Build</c>.
/// </remarks>
internal sealed class SecretDecryptionView
{
    private ConcurrentDictionary<string, Dictionary<string, bool>> _isSecretByTenantKey = new(StringComparer.Ordinal);
    private volatile IConfigEncryptor? _encryptor;

    /// <summary>
    /// Replaces the per-tenant secret-flag dictionary with <paramref name="snapshot"/>.
    /// Called by the polling provider at the end of every reload tick.
    /// </summary>
    public void UpdateSecretFlags(ConcurrentDictionary<string, Dictionary<string, bool>> snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        Volatile.Write(ref _isSecretByTenantKey, snapshot);
    }

    /// <summary>
    /// Sets the encryptor used for on-demand decryption. Throws if a different encryptor
    /// instance has already been set; idempotent when called with the same instance.
    /// </summary>
    public void SetEncryptor(IConfigEncryptor encryptor)
    {
        ArgumentNullException.ThrowIfNull(encryptor);

        var existing = _encryptor;
        if (existing is not null && !ReferenceEquals(existing, encryptor))
        {
            throw new InvalidOperationException(
                "DbConfigConfigurationProvider already has an encryptor set. " +
                "SetEncryptor may only be called once (or repeatedly with the same instance). " +
                "If you intended to swap encryptors, restart the host with a fresh registration.");
        }

        _encryptor = encryptor;
    }

    /// <summary>
    /// Returns the plaintext for <paramref name="key"/> in <paramref name="tenantId"/>'s
    /// bag. Non-secret values pass through verbatim. Secret values are decrypted via the
    /// configured <see cref="IConfigEncryptor"/>; reading a secret before
    /// <see cref="SetEncryptor"/> has been called throws
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    public string? Decrypt(string tenantId, string key, string? rawValue)
    {
        var secretSnapshot = Volatile.Read(ref _isSecretByTenantKey);
        var isSecret = secretSnapshot.TryGetValue(tenantId, out var tenantSecrets) &&
                       tenantSecrets.TryGetValue(key, out var s) && s;

        if (!isSecret)
        {
            return rawValue;
        }

        if (_encryptor is null)
        {
            throw new InvalidOperationException(
                $"Cannot read secret config value '{key}' before host.Build() has returned. " +
                "Move this read into a request handler, hosted service, or OnStarted callback. " +
                "Non-secret values are unaffected.");
        }

        return rawValue is null ? null : _encryptor.Unprotect(rawValue);
    }
}

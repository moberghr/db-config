namespace DbConfig.Core;

/// <summary>
/// Encrypts and decrypts configuration values for entries marked <c>IsSecret=true</c>.
/// </summary>
/// <remarks>
/// The default implementation uses ASP.NET Core Data Protection (filesystem key ring)
/// and is registered automatically by <c>AddDbConfig</c> via
/// <c>TryAddSingleton&lt;IConfigEncryptor, DataProtectionConfigEncryptor&gt;()</c>.
/// Consumers can register a custom implementation (e.g. wrapping AWS KMS or Azure Key Vault)
/// before calling <c>AddDbConfig</c> — the <c>TryAddSingleton</c> registration will be a no-op
/// when a prior registration exists.
/// </remarks>
public interface IConfigEncryptor
{
    /// <summary>
    /// Encrypts a plaintext config value. Called only for entries where <c>IsSecret=true</c>.
    /// Returns a stable string representation suitable for storage in <c>nvarchar(max)</c>.
    /// </summary>
    /// <param name="plaintext">The plaintext value to encrypt.</param>
    /// <returns>The encrypted representation of <paramref name="plaintext"/>.</returns>
    string Protect(string plaintext);

    /// <summary>
    /// Decrypts a previously protected value. Throws if the input is not a valid
    /// protected payload (e.g. key revoked, payload corrupted, or the <c>IsSecret</c> bit
    /// was flipped after encryption).
    /// </summary>
    /// <param name="ciphertext">The protected payload returned by <see cref="Protect"/>.</param>
    /// <returns>The original plaintext value.</returns>
    string Unprotect(string ciphertext);
}

namespace DbConfig.Core;

/// <summary>
/// No-operation <see cref="IConfigEncryptor"/> that returns values verbatim without encryption.
/// Used as the default fallback for <see cref="InMemoryConfigStore"/> so tests that do not
/// care about real cryptography can run without Data Protection setup.
/// </summary>
internal sealed class PassthroughConfigEncryptor : IConfigEncryptor
{
    /// <inheritdoc/>
    public string Protect(string plaintext) => plaintext;

    /// <inheritdoc/>
    public string Unprotect(string ciphertext) => ciphertext;
}

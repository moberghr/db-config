using DbConfig.Core;
using Microsoft.AspNetCore.DataProtection;

namespace DbConfig.EntityFrameworkCore;

/// <summary>
/// <see cref="IConfigEncryptor"/> implementation backed by ASP.NET Core Data Protection.
/// Protects values under the purpose string <c>DbConfig.SecretValue.v1</c>.
/// </summary>
/// <remarks>
/// Registered automatically by <c>AddDbConfig</c> when no consumer-supplied
/// <c>IConfigEncryptor</c> instance is found in the host services.
/// Consumers that need custom key storage (e.g. Azure Key Vault, AWS Parameter Store) should
/// call <c>services.AddSingleton&lt;IConfigEncryptor&gt;(myInstance)</c> before calling <c>AddDbConfig</c>.
/// </remarks>
public sealed class DataProtectionConfigEncryptor : IConfigEncryptor
{
    private readonly IDataProtector _protector;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataProtectionConfigEncryptor"/> class.
    /// </summary>
    /// <param name="provider">The Data Protection provider from which the protector is created.</param>
    public DataProtectionConfigEncryptor(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector("DbConfig.SecretValue.v1");
    }

    /// <inheritdoc/>
    public string Protect(string plaintext) => _protector.Protect(plaintext);

    /// <inheritdoc/>
    public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);
}

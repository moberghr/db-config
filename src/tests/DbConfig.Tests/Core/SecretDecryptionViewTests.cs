using System.Collections.Concurrent;
using DbConfig.Core;
using DbConfig.Tests.TestData;
using Shouldly;

namespace DbConfig.Tests.Core;

/// <summary>
/// Direct unit tests for <see cref="SecretDecryptionView"/> — the slice extracted from
/// <see cref="DbConfigConfigurationProvider"/> in v0.14.0. Covers the contract surface
/// that previously required spinning up the full provider to exercise.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecretDecryptionViewTests
{
    [TimedFact]
    public void Decrypt_NonSecretEntry_ReturnsRawValueVerbatim_NoEncryptorNeeded()
    {
        var view = new SecretDecryptionView();
        view.UpdateSecretFlags(BuildFlags((string.Empty, "Key", false)));

        // No encryptor set — non-secret values still decrypt-pass-through.
        view.Decrypt(string.Empty, "Key", "plain-value").ShouldBe("plain-value");
    }

    [TimedFact]
    public void Decrypt_NonSecretEntry_NullRawValue_ReturnsNull()
    {
        var view = new SecretDecryptionView();
        view.UpdateSecretFlags(BuildFlags((string.Empty, "Key", false)));

        view.Decrypt(string.Empty, "Key", rawValue: null).ShouldBeNull();
    }

    [TimedFact]
    public void Decrypt_UnknownKey_TreatedAsNonSecret_PassesValueThrough()
    {
        // Flag dictionary doesn't know about "Other" — defaults to non-secret.
        var view = new SecretDecryptionView();
        view.UpdateSecretFlags(BuildFlags((string.Empty, "Known", true)));

        view.Decrypt(string.Empty, "Other", "raw").ShouldBe("raw");
    }

    [TimedFact]
    public void Decrypt_SecretEntry_WithoutEncryptor_Throws()
    {
        var view = new SecretDecryptionView();
        view.UpdateSecretFlags(BuildFlags((string.Empty, "Secret", true)));

        var ex = Should.Throw<InvalidOperationException>(
            () => view.Decrypt(string.Empty, "Secret", "ciphertext"));

        ex.Message.ShouldContain("Secret");
        ex.Message.ShouldContain("host.Build()");
    }

    [TimedFact]
    public void Decrypt_SecretEntry_WithEncryptor_DecryptsViaUnprotect()
    {
        var view = new SecretDecryptionView();
        view.UpdateSecretFlags(BuildFlags((string.Empty, "Secret", true)));
        view.SetEncryptor(new ReversingEncryptor());

        // ReversingEncryptor returns the input reversed — proves Unprotect was actually called.
        view.Decrypt(string.Empty, "Secret", "abc").ShouldBe("cba");
    }

    [TimedFact]
    public void Decrypt_SecretEntry_NullRawValue_WithEncryptor_ReturnsNullWithoutCallingUnprotect()
    {
        var encryptor = new ReversingEncryptor();
        var view = new SecretDecryptionView();
        view.UpdateSecretFlags(BuildFlags((string.Empty, "Secret", true)));
        view.SetEncryptor(encryptor);

        view.Decrypt(string.Empty, "Secret", rawValue: null).ShouldBeNull();
        encryptor.UnprotectCallCount.ShouldBe(0, "null ciphertext must not be passed to Unprotect");
    }

    [TimedFact]
    public void Decrypt_TenantSpecificSecret_UsesTenantFlagNotGlobalFallback()
    {
        // Same key is secret in tenant Acme but not in global. Decrypt under the Acme bag
        // must see secret=true and route through the encryptor; the global bag's flag is
        // irrelevant to the tenant lookup.
        var view = new SecretDecryptionView();
        view.UpdateSecretFlags(BuildFlags(
            (string.Empty, "Key", false),
            ("Acme", "Key", true)));
        view.SetEncryptor(new ReversingEncryptor());

        view.Decrypt("Acme", "Key", "abc").ShouldBe("cba");
        view.Decrypt(string.Empty, "Key", "xyz").ShouldBe("xyz");
    }

    [TimedFact]
    public void SetEncryptor_SameInstanceTwice_NoOp()
    {
        var view = new SecretDecryptionView();
        var encryptor = new ReversingEncryptor();

        view.SetEncryptor(encryptor);
        Should.NotThrow(() => view.SetEncryptor(encryptor));
    }

    [TimedFact]
    public void SetEncryptor_DifferentInstance_Throws()
    {
        var view = new SecretDecryptionView();
        view.SetEncryptor(new ReversingEncryptor());

        var ex = Should.Throw<InvalidOperationException>(
            () => view.SetEncryptor(new ReversingEncryptor()));

        ex.Message.ShouldContain("already has an encryptor");
    }

    [TimedFact]
    public void SetEncryptor_NullArgument_ThrowsArgumentNullException()
    {
        var view = new SecretDecryptionView();
        Should.Throw<ArgumentNullException>(() => view.SetEncryptor(null!));
    }

    [TimedFact]
    public void UpdateSecretFlags_ReplacesPriorSnapshot()
    {
        var view = new SecretDecryptionView();
        view.SetEncryptor(new ReversingEncryptor());

        // Snapshot 1: Key is secret.
        view.UpdateSecretFlags(BuildFlags((string.Empty, "Key", true)));
        view.Decrypt(string.Empty, "Key", "abc").ShouldBe("cba");

        // Snapshot 2: Key is no longer flagged secret — decryption stops happening.
        view.UpdateSecretFlags(BuildFlags((string.Empty, "Key", false)));
        view.Decrypt(string.Empty, "Key", "abc").ShouldBe("abc");
    }

    [TimedFact]
    public void UpdateSecretFlags_NullArgument_ThrowsArgumentNullException()
    {
        var view = new SecretDecryptionView();
        Should.Throw<ArgumentNullException>(() => view.UpdateSecretFlags(null!));
    }

    private static ConcurrentDictionary<string, Dictionary<string, bool>> BuildFlags(
        params (string TenantId, string Key, bool IsSecret)[] entries)
    {
        var result = new ConcurrentDictionary<string, Dictionary<string, bool>>(StringComparer.Ordinal);
        foreach (var (tenant, key, isSecret) in entries)
        {
            if (!result.TryGetValue(tenant, out var bag))
            {
                bag = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                result[tenant] = bag;
            }

            bag[key] = isSecret;
        }

        return result;
    }

    private sealed class ReversingEncryptor : IConfigEncryptor
    {
        public int ProtectCallCount { get; private set; }

        public int UnprotectCallCount { get; private set; }

        public string Protect(string plaintext)
        {
            ProtectCallCount++;
            return Reverse(plaintext);
        }

        public string Unprotect(string ciphertext)
        {
            UnprotectCallCount++;
            return Reverse(ciphertext);
        }

        private static string Reverse(string s)
        {
            var chars = s.ToCharArray();
            Array.Reverse(chars);
            return new string(chars);
        }
    }
}

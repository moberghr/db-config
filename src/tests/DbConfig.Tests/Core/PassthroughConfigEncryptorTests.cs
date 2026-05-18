using DbConfig.Core;
using DbConfig.Tests.TestData;
using Shouldly;

namespace DbConfig.Tests.Core;

[Trait("Category", "Unit")]
public sealed class PassthroughConfigEncryptorTests
{
    private readonly PassthroughConfigEncryptor _encryptor = new();

    [TimedFact]
    public void Protect_ReturnsInputVerbatim()
    {
        const string input = "my-secret-value";

        var result = _encryptor.Protect(input);

        result.ShouldBe(input);
    }

    [TimedFact]
    public void Unprotect_ReturnsInputVerbatim()
    {
        const string input = "some-ciphertext-or-plaintext";

        var result = _encryptor.Unprotect(input);

        result.ShouldBe(input);
    }

    [TimedFact]
    public void RoundTrip_IsIdentity()
    {
        const string plaintext = "round-trip-value";

        var protected_ = _encryptor.Protect(plaintext);
        var unprotected = _encryptor.Unprotect(protected_);

        unprotected.ShouldBe(plaintext);
    }
}

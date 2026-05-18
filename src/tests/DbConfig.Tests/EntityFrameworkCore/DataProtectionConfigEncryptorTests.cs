using DbConfig.EntityFrameworkCore;
using DbConfig.Tests.TestData;
using Microsoft.AspNetCore.DataProtection;
using Shouldly;

namespace DbConfig.Tests.EntityFrameworkCore;

[Trait("Category", "Unit")]
public sealed class DataProtectionConfigEncryptorTests
{
    [TimedFact]
    public void Protect_OutputIsDifferentFromInput()
    {
        var encryptor = new DataProtectionConfigEncryptor(DataProtectionProvider.Create("test-app"));
        const string plaintext = "super-secret-value";

        var ciphertext = encryptor.Protect(plaintext);

        ciphertext.ShouldNotBe(plaintext);
        ciphertext.ShouldNotBeNullOrEmpty();
    }

    [TimedFact]
    public void Unprotect_AfterProtect_ReturnsOriginal()
    {
        var encryptor = new DataProtectionConfigEncryptor(DataProtectionProvider.Create("test-app"));
        const string plaintext = "my-secret-password-123";

        var ciphertext = encryptor.Protect(plaintext);
        var recovered = encryptor.Unprotect(ciphertext);

        recovered.ShouldBe(plaintext);
    }

    [TimedFact]
    public void Unprotect_OnGarbageInput_Throws()
    {
        var encryptor = new DataProtectionConfigEncryptor(DataProtectionProvider.Create("test-app"));

        var exception = Record.Exception(
            () => encryptor.Unprotect("not-a-valid-ciphertext"));

        exception.ShouldNotBeNull();
    }

    [TimedFact]
    public void TwoProtectorsWithSameKeyRing_CanUnprotectEachOther()
    {
        // Both protectors are created from the same application name which produces the same
        // ephemeral in-process key ring, so they must be able to decrypt each other's ciphertext.
        var provider = DataProtectionProvider.Create("shared-key-ring-test");
        var encryptor1 = new DataProtectionConfigEncryptor(provider);
        var encryptor2 = new DataProtectionConfigEncryptor(provider);

        const string plaintext = "cross-instance-value";
        var ciphertext = encryptor1.Protect(plaintext);

        var recovered = encryptor2.Unprotect(ciphertext);

        recovered.ShouldBe(plaintext);
    }
}

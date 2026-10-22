using DbConfig.Core;
using DbConfig.Http.Endpoints;
using DbConfig.Tests.TestData;
using Shouldly;

namespace DbConfig.Tests.Http;

/// <summary>
/// Pins <see cref="WriteOnlyRedaction"/> at its source: write-only records lose every value field
/// and keep everything else; other records pass through untouched.
/// </summary>
[Trait("Category", "Unit")]
public sealed class WriteOnlyRedactionTests
{
    private static readonly DateTimeOffset Modified = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    [TimedFact]
    public void Redact_WriteOnlyEntry_NullsValueOnly()
    {
        var entry = new ConfigEntryRecord("App", "Env", "Acme", "Key", "secret", true, Modified, "tester", IsWriteOnly: true);

        var redacted = WriteOnlyRedaction.Redact(entry);

        redacted.ShouldBe(entry with { Value = null });
    }

    [TimedFact]
    public void Redact_PlainSecretEntry_ReturnsUnchanged()
    {
        var entry = new ConfigEntryRecord("App", "Env", string.Empty, "Key", "secret", true, Modified, "tester");

        var redacted = WriteOnlyRedaction.Redact(entry);

        redacted.ShouldBe(entry);
    }

    [TimedFact]
    public void Redact_WriteOnlyAuditRow_NullsBothValues()
    {
        var row = AuditRow(isWriteOnly: true);

        var redacted = WriteOnlyRedaction.Redact(row);

        redacted.ShouldBe(row with { OldValue = null, NewValue = null });
    }

    [TimedFact]
    public void Redact_PlainAuditRow_ReturnsUnchanged()
    {
        var row = AuditRow(isWriteOnly: false);

        var redacted = WriteOnlyRedaction.Redact(row);

        redacted.ShouldBe(row);
    }

    private static ConfigAuditEntryRecord AuditRow(bool isWriteOnly)
    {
        return new ConfigAuditEntryRecord(
            Guid.NewGuid(), "App", "Env", string.Empty, "Key", "old", "new", true, ConfigAuditAction.Update, Modified, "tester", isWriteOnly);
    }
}

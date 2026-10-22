using DbConfig.Core;

namespace DbConfig.Http.Endpoints;

// Write-only values may be replaced over HTTP but never read back. Every endpoint that
// serializes an entry or an audit row passes it through here first. Audit stores already flag
// rows whose key is write-only now, so the row flag alone decides.
internal static class WriteOnlyRedaction
{
    internal static ConfigEntryRecord Redact(ConfigEntryRecord entry)
    {
        return entry.IsWriteOnly
            ? entry with { Value = null }
            : entry;
    }

    internal static ConfigAuditEntryRecord Redact(ConfigAuditEntryRecord row)
    {
        return row.IsWriteOnly
            ? row with { OldValue = null, NewValue = null }
            : row;
    }

    internal static IEnumerable<ConfigEntryRecord> Redact(IEnumerable<ConfigEntryRecord> entries)
    {
        return entries.Select(Redact);
    }

    internal static IEnumerable<ConfigAuditEntryRecord> Redact(IEnumerable<ConfigAuditEntryRecord> rows)
    {
        return rows.Select(Redact);
    }
}

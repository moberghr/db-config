using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DbConfig.EntityFrameworkCore;

/// <summary>
/// Forces every <see cref="DateTime"/> value going through EF Core to be UTC, both on write
/// and on read. SQL Server's <c>datetime2</c> column doesn't carry a time zone, so a non-UTC
/// value written without normalization silently loses meaning — the consumer who reads it back
/// can't tell what wall clock the timestamp was captured in. This converter is defense in
/// depth: even if a future code path accidentally passes <see cref="DateTime.Now"/>, the value
/// is normalized to UTC before persistence and tagged <see cref="DateTimeKind.Utc"/> on read
/// so downstream comparisons are unambiguous.
/// </summary>
public sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(
            write => write.Kind == DateTimeKind.Utc
                ? write
                : write.ToUniversalTime(),
            read => DateTime.SpecifyKind(read, DateTimeKind.Utc))
    {
    }
}

/// <summary>
/// Forces every <see cref="DateTimeOffset"/> value going through EF Core to be UTC on write.
/// SQL Server's <c>datetimeoffset</c> stores the offset alongside the instant, so comparisons
/// are still correct without normalization; but storing every row with offset zero makes the
/// raw column inspection predictable and avoids subtle ordering surprises in raw SQL queries
/// that compare by wall-clock components rather than by instant. Reads pass through unchanged
/// (the value already has the correct offset persisted).
/// </summary>
public sealed class UtcDateTimeOffsetConverter : ValueConverter<DateTimeOffset, DateTimeOffset>
{
    public UtcDateTimeOffsetConverter()
        : base(
            write => write.ToUniversalTime(),
            read => read)
    {
    }
}

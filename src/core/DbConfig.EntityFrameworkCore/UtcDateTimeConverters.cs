using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DbConfig.EntityFrameworkCore;

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

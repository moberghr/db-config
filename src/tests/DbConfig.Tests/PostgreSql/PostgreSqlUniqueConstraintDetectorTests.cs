using DbConfig.Provider.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace DbConfig.Tests.PostgreSql;

/// <summary>
/// Unit tests for <see cref="PostgreSqlUniqueConstraintDetector"/>.
///
/// Positive-case coverage (PostgresException with SqlState "23505") is exercised by the
/// existing <see cref="PostgreSqlStoreCrudTests.Upsert_Concurrent_LastWriterWins_NoException"/>
/// integration test which runs against a real PostgreSQL container. <c>PostgresException</c>
/// requires internal PostgreSQL wire-protocol parsing to construct and cannot be directly
/// instantiated in test code, so this class verifies only the negative (non-PostgresException
/// inner) path as a type-gate smoke test.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PostgreSqlUniqueConstraintDetectorTests
{
    [Fact]
    public void IsUniqueConstraintViolation_OnNonPostgresInnerException_ReturnsFalse()
    {
        var detector = new PostgreSqlUniqueConstraintDetector();
        var exception = new DbUpdateException("msg", new InvalidOperationException("not a postgres exception"));

        var result = detector.IsUniqueConstraintViolation(exception);

        result.ShouldBeFalse();
    }
}

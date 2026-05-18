using DbConfig.Provider.SqlServer;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace DbConfig.Tests.SqlServer;

/// <summary>
/// Unit tests for <see cref="SqlServerUniqueConstraintDetector"/>.
///
/// Positive-case coverage (SqlException numbers 2627 and 2601) is exercised by the
/// existing <see cref="SqlServerStoreCrudTests.Upsert_Concurrent_LastWriterWins_NoException"/>
/// integration test which runs against a real SQL Server container. <c>SqlException</c>
/// has an internal constructor and cannot be directly instantiated in test code, so this
/// class verifies only the negative (non-SqlException inner) path as a type-gate smoke test.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SqlServerUniqueConstraintDetectorTests
{
    [Fact]
    public void IsUniqueConstraintViolation_OnNonSqlInnerException_ReturnsFalse()
    {
        var detector = new SqlServerUniqueConstraintDetector();
        var exception = new DbUpdateException("msg", new InvalidOperationException("not a sql exception"));

        var result = detector.IsUniqueConstraintViolation(exception);

        result.ShouldBeFalse();
    }
}

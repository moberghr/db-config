using DbConfig.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DbConfig.Provider.PostgreSql;

public sealed class PostgreSqlUniqueConstraintDetector : IUniqueConstraintDetector
{
    public bool IsUniqueConstraintViolation(DbUpdateException exception)
        => exception.InnerException is PostgresException { SqlState: "23505" };
}

using DbConfig.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DbConfig.Provider.SqlServer;

public sealed class SqlServerUniqueConstraintDetector : IUniqueConstraintDetector
{
    public bool IsUniqueConstraintViolation(DbUpdateException exception)
        => exception.InnerException is SqlException sql && (sql.Number is 2627 or 2601);
}

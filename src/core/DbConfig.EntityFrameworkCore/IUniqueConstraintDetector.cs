using Microsoft.EntityFrameworkCore;

namespace DbConfig.EntityFrameworkCore;

/// <summary>Per-provider strategy: identifies whether a <see cref="DbUpdateException"/> represents a
/// unique-constraint violation. Implementations live in provider packages so Core has no
/// knowledge of provider-specific exception types.</summary>
public interface IUniqueConstraintDetector
{
    bool IsUniqueConstraintViolation(DbUpdateException exception);
}

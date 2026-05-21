using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace DbConfig.EntityFrameworkCore;

/// <summary>
/// Includes the configured schema (from <see cref="DbConfigOptionsExtension.Schema"/>) in
/// the EF Core model cache key. Without this, the first DbContext to build the model caches
/// it for whatever schema it sees first; subsequent contexts built with a different schema
/// would silently reuse the stale model.
/// </summary>
internal sealed class DbConfigModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
    {
        var schema = context.GetService<IDbContextOptions>().GetDbConfigSchema();
        return (context.GetType(), schema, designTime);
    }
}

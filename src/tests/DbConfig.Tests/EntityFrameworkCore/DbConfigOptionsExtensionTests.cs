using DbConfig.EntityFrameworkCore;
using DbConfig.Tests.TestData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Shouldly;

namespace DbConfig.Tests.EntityFrameworkCore;

/// <summary>
/// Unit-level coverage for <see cref="DbConfigOptionsExtension"/> and the
/// <c>UseDbConfigSchema</c> builder extension. Verifies the immutable-extension pattern:
/// repeated calls produce the latest schema, generic + non-generic overloads agree,
/// and <see cref="DbConfigOptionsExtension.WithSchema"/> is a pure copy that does not
/// mutate the source.
/// </summary>
[Trait("Category", "Unit")]
public sealed class DbConfigOptionsExtensionTests
{
    [TimedFact]
    public void UseDbConfigSchema_StoresSchema_AccessibleViaGetDbConfigSchema()
    {
        var builder = new DbContextOptionsBuilder<DbConfigDbContext>();
        builder.UseDbConfigSchema("my_schema");

        var schema = ((IDbContextOptions)builder.Options).GetDbConfigSchema();
        schema.ShouldBe("my_schema");
    }

    [TimedFact]
    public void UseDbConfigSchema_NullValue_GetDbConfigSchemaReturnsNull()
    {
        var builder = new DbContextOptionsBuilder<DbConfigDbContext>();
        builder.UseDbConfigSchema(null);

        var schema = ((IDbContextOptions)builder.Options).GetDbConfigSchema();
        schema.ShouldBeNull();
    }

    [TimedFact]
    public void GetDbConfigSchema_NoExtensionRegistered_ReturnsNull()
    {
        var builder = new DbContextOptionsBuilder<DbConfigDbContext>();

        var schema = ((IDbContextOptions)builder.Options).GetDbConfigSchema();
        schema.ShouldBeNull();
    }

    [TimedFact]
    public void UseDbConfigSchema_CalledTwice_LatestWins()
    {
        var builder = new DbContextOptionsBuilder<DbConfigDbContext>();
        builder.UseDbConfigSchema("first");
        builder.UseDbConfigSchema("second");

        ((IDbContextOptions)builder.Options).GetDbConfigSchema().ShouldBe("second");
    }

    [TimedFact]
    public void UseDbConfigSchema_GenericOverload_PreservesTypedBuilderInFluentChain()
    {
        // The generic overload must return DbContextOptionsBuilder<TContext>, not the
        // non-generic base, so consumers can keep chaining .UseSqlServer<TContext>(...)
        // without losing the type parameter.
        var builder = new DbContextOptionsBuilder<DbConfigDbContext>();

        var chained = builder.UseDbConfigSchema("typed");

        chained.ShouldBeSameAs(builder);
        ((IDbContextOptions)chained.Options).GetDbConfigSchema().ShouldBe("typed");
    }

    [TimedFact]
    public void WithSchema_DoesNotMutateOriginalExtension()
    {
        var original = new DbConfigOptionsExtension();
        var withA = original.WithSchema("alpha");
        var withB = withA.WithSchema("beta");

        original.Schema.ShouldBeNull();
        withA.Schema.ShouldBe("alpha");
        withB.Schema.ShouldBe("beta");
    }

    [TimedFact]
    public void ExtensionInfo_ShouldUseSameServiceProvider_RespectsSchemaEquality()
    {
        var sameA = new DbConfigOptionsExtension().WithSchema("x");
        var sameB = new DbConfigOptionsExtension().WithSchema("x");
        var different = new DbConfigOptionsExtension().WithSchema("y");

        sameA.Info.ShouldUseSameServiceProvider(sameB.Info).ShouldBeTrue();
        sameA.Info.ShouldUseSameServiceProvider(different.Info).ShouldBeFalse();
    }

    [TimedFact]
    public void ExtensionInfo_PopulatesLogFragmentAndDebugInfo()
    {
        var ext = new DbConfigOptionsExtension().WithSchema("billing");

        ext.Info.LogFragment.ShouldContain("billing");

        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        ext.Info.PopulateDebugInfo(dict);
        dict["DbConfig:Schema"].ShouldBe("billing");
    }

    [TimedFact]
    public void ExtensionInfo_NullSchema_DebugInfoShowsDefaultSentinel()
    {
        var ext = new DbConfigOptionsExtension().WithSchema(null);

        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        ext.Info.PopulateDebugInfo(dict);
        dict["DbConfig:Schema"].ShouldBe("(default)");
    }
}

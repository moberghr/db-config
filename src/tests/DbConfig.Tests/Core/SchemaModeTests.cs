using DbConfig.Core;
using DbConfig.Tests.TestData;
using Shouldly;

namespace DbConfig.Tests.Core;

[Trait("Category", "Unit")]
public sealed class SchemaModeTests
{
    [TimedFact]
    public void SchemaMode_Default_IsCreateIfMissing()
    {
        var options = new DbConfigOptions();

        options.SchemaMode.ShouldBe(SchemaMode.CreateIfMissing);
    }
}

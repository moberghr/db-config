namespace DbConfig.Core;

internal sealed class NullTenantResolver : ITenantResolver
{
    public static readonly NullTenantResolver Instance = new();

    private NullTenantResolver()
    {
    }

    public string? Resolve() => null;
}

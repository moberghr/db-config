namespace DbConfig.Core;

/// <summary>
/// Computes the configuration section prefix used by the typed-bind convenience overloads
/// on <see cref="IConfigStore"/> (<c>GetAsync&lt;T&gt;</c> and <c>GetForTenantAsync&lt;T&gt;</c>).
/// </summary>
/// <remarks>
/// <para>
/// The prefix is <c>typeof(T).Name + ":"</c> with the CLR generic-arity suffix stripped.
/// For <c>StripeOptions</c> the prefix is <c>"StripeOptions:"</c>. For a generic type
/// <c>MyGeneric&lt;int&gt;</c> the raw <c>typeof(T).Name</c> is <c>"MyGeneric`1"</c> —
/// the backtick + arity is stripped so the prefix is <c>"MyGeneric:"</c>.
/// </para>
/// <para>
/// Caveat: multiple instantiations of the same open generic
/// (e.g. <c>MyGeneric&lt;int&gt;</c> and <c>MyGeneric&lt;string&gt;</c>) collide on the
/// same section. If you want separate sections for each instantiation, define a
/// non-generic outer type that wraps the generic and bind that instead.
/// </para>
/// </remarks>
public static class TypedSectionPrefix
{
    /// <summary>
    /// Returns the section prefix (<c>"&lt;Name&gt;:"</c>) for the given type
    /// <typeparamref name="T"/>, with the CLR generic-arity suffix stripped.
    /// </summary>
    public static string For<T>() => For(typeof(T));

    /// <summary>
    /// Returns the section prefix (<c>"&lt;Name&gt;:"</c>) for the given <paramref name="type"/>,
    /// with the CLR generic-arity suffix stripped.
    /// </summary>
    public static string For(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var name = type.Name;
        var backtickIndex = name.IndexOf('`', StringComparison.Ordinal);
        var sectionName = backtickIndex >= 0 ? name[..backtickIndex] : name;

        return sectionName + ":";
    }
}

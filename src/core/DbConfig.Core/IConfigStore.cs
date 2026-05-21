namespace DbConfig.Core;

/// <summary>
/// Composite over every backing-store contract: <see cref="IConfigReader"/>,
/// <see cref="IConfigSnapshotReader"/>, <see cref="IConfigWatermark"/>,
/// <see cref="IConfigWriter"/>, <see cref="IConfigQuery"/>, and
/// <see cref="IAmbientConfigReader"/>. Consumers that only need a subset (e.g. HTTP read
/// endpoints) SHOULD depend on the relevant narrow interface instead — the composite
/// exists primarily for the built-in EF Core store and tests that exercise the full
/// surface.
/// </summary>
/// <remarks>
/// Implemented by provider packages (SQL Server, PostgreSQL via <c>EfCoreConfigStore</c>) and
/// by <see cref="InMemoryConfigStore"/> for testing. Custom stores backed by Redis, a flat
/// file, or any other non-EF backend may implement only the contracts they support — e.g.
/// a write-through cache may implement <see cref="IConfigReader"/> and
/// <see cref="IConfigWriter"/> without <see cref="IConfigQuery"/> or
/// <see cref="IAmbientConfigReader"/>.
/// </remarks>
public interface IConfigStore :
    IConfigReader,
    IConfigPollingStore,
    IConfigWriter,
    IConfigQuery,
    IAmbientConfigReader;

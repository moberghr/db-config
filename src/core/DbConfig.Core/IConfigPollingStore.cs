namespace DbConfig.Core;

/// <summary>
/// Narrow composite consumed by <see cref="DbConfigConfigurationProvider"/>. Combines
/// the multi-tenant bulk read and watermark contracts that the polling loop actually
/// uses, without forcing the implementer to also support writes, ambient reads, or the
/// admin-UI flat-scan query.
/// </summary>
/// <remarks>
/// Built-in stores (<see cref="InMemoryConfigStore"/> and the EF-Core-backed store)
/// implement this transitively via <see cref="IConfigStore"/>. Custom polling-backing
/// stores (e.g. a Redis-backed cache) may implement this directly without committing to
/// the full <see cref="IConfigStore"/> surface.
/// </remarks>
public interface IConfigPollingStore : IConfigSnapshotReader, IConfigWatermark;

namespace DbConfig.Core;

/// <summary>
/// Allows the HTTP reload endpoint to trigger an immediate configuration reload
/// without waiting for the next polling interval.
/// </summary>
public interface IDbConfigReloadSignal
{
    /// <summary>Schedule an immediate reload of the configuration provider. Idempotent.</summary>
    void Trigger();
}

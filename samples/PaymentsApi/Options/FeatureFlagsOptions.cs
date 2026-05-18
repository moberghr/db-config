namespace PaymentsApi.Options;

public sealed class FeatureFlagsOptions
{
    // Per-tenant override
    public bool NewCheckout { get; set; }

    // Per-tenant override
    public bool Require3DS { get; set; }

    // Global rollout flag
    public bool BetaSplitPayments { get; set; }
}

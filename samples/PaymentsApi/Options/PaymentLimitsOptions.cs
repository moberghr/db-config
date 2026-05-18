namespace PaymentsApi.Options;

public sealed class PaymentLimitsOptions
{
    // Per-tenant override
    public int MaxChargeAmount { get; set; } = 50000;

    // Global default
    public int DailyChargeCap { get; set; } = 1000000;
}

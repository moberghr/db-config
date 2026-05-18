namespace PaymentsApi.Options;

public sealed class StripeOptions
{
    // Per-tenant, IsSecret = true
    public string ApiKey { get; set; } = string.Empty;

    // Global, IsSecret = true
    public string WebhookSecret { get; set; } = string.Empty;

    // Per-tenant override, plaintext
    public string DefaultCurrency { get; set; } = "USD";

    // Global, plaintext
    public int IdempotencyWindowSeconds { get; set; } = 60;
}

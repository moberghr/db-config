namespace PaymentsApi.Options;

public sealed class NotificationsOptions
{
    // Per-tenant, IsSecret = true
    public string SlackWebhook { get; set; } = string.Empty;

    // Per-tenant, plaintext
    public string OnFailureEmail { get; set; } = string.Empty;
}

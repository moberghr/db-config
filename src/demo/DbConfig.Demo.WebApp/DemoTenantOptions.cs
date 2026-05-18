namespace DbConfig.Demo.WebApp;

public sealed class DemoTenantOptions
{
    public string DisplayName { get; set; } = "Unnamed Tenant";

    public string StripeApiKey { get; set; } = string.Empty;

    public bool FeatureNewCheckoutEnabled { get; set; }
}

namespace PaymentsApi;

public sealed record ChargeRequest(int Amount, string? Currency, string CustomerId);

public sealed record RefundRequest(string ChargeId, int? Amount);

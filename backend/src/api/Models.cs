namespace api;
public readonly record struct PaymentClientReq(Guid CorrelationId, decimal Amount);
public readonly record struct PaymentClientProcessed(Guid CorrelationId, decimal Amount, DateTime RequestedAt);
public record PaymentProcessorsConfig(Dictionary<string, string> Default, Dictionary<string, string> Fallback);
public record HealthCheckResponse(bool failing, int minResponseTime);
public class ServiceAvaliability
{
    public bool IsServiceDefault { get; set; } = true;
    public bool IsServicesOn { get; set; } = true;
};
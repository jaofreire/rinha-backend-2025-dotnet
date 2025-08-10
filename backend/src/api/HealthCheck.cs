namespace api;

public class HealthCheck(HttpClient client, ServiceAvaliability serviceAvaliability, PaymentProcessorsConfig paymentProcessorsConfig) : BackgroundService
{
    private readonly HttpClient _client = client;
    private readonly PaymentProcessorsConfig _paymentProcessorsConfig = paymentProcessorsConfig;
    private readonly ServiceAvaliability _serviceAvaliability = serviceAvaliability;
    private const string URL_HEALTH_CHECK = "/payments/service-health";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            Console.WriteLine("HealthCheckService is Running");
            var healthCheckDefault = await DefaultHealthCheck();
            if (healthCheckDefault is not null && !healthCheckDefault.failing && healthCheckDefault.minResponseTime <= 20)
            {
                _serviceAvaliability.IsServiceDefault = true;
                _serviceAvaliability.IsServicesOn = true;
            }
            else
            {
                var healthCheckFallback = await FallbackHealthCheck();
                if (healthCheckFallback is not null && !healthCheckFallback.failing)
                {
                    _serviceAvaliability.IsServiceDefault = false;
                    _serviceAvaliability.IsServicesOn = true;
                }
                else
                {
                    _serviceAvaliability.IsServiceDefault = false;
                    _serviceAvaliability.IsServicesOn = false;
                }
            }
            await Task.Delay(5_000, stoppingToken);
        }
    }

    private async Task<HealthCheckResponse?> DefaultHealthCheck()
    {
        var response = await _client.GetAsync(_paymentProcessorsConfig.Default["default"] + URL_HEALTH_CHECK);

        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadFromJsonAsync<HealthCheckResponse>();
            Console.WriteLine($"Default: Failing - {content.failing}, MinResponseTime - {content.minResponseTime}");
            return content;
        }

        Console.WriteLine($" DefaultHealthCheck StatusCode -  {response.StatusCode}");
            

        return null;
    }

    private async Task<HealthCheckResponse?> FallbackHealthCheck()
    {
        var response = await _client.GetAsync(_paymentProcessorsConfig.Fallback["fallback"] + URL_HEALTH_CHECK);

        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadFromJsonAsync<HealthCheckResponse>();
            Console.WriteLine($"Fallbacl: Failing - {content.failing}, MinResponseTime - {content.minResponseTime}");
            return content;
        }

        Console.WriteLine($" FallbackHealthCheck StatusCode -  {response.StatusCode}");

        return null;
    }
}
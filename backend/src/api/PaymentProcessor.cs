using Npgsql;
using System.Text;
using System.Text.Json;
using System.Diagnostics;

namespace api;

public static class TryPaymentHandler
{
    public static async ValueTask ExecuteAsync(
        PaymentClientProcessed payment,
        HttpClient client,
        ServiceAvaliability serviceAvaliability,
        PaymentProcessorsConfig paymentProcessorsConfig,
        QueueManager queue,
        NpgsqlDataSource dataSource
        )
    {
        if (serviceAvaliability.IsServicesOn is false)
        {
            var paymentReqError = new PaymentClientReq(payment.CorrelationId, payment.Amount);
            await queue.Writer.WriteAsync(paymentReqError);
        }

        if (serviceAvaliability.IsServiceDefault && serviceAvaliability.IsServicesOn is true)
        {
            var stopWatch = Stopwatch.StartNew();
            var response = await SendPayment(client, $"{paymentProcessorsConfig.Default["default"]}/payments", payment);

            stopWatch.Stop();

            if (response.IsSuccessStatusCode)
            {
                serviceAvaliability.IsServicesOn = true;

                if (stopWatch.ElapsedMilliseconds > 100)
                {
                    serviceAvaliability.IsServiceDefault = false;
                }

                await PersistPayment(dataSource, payment, "Default");

                return;
            }

            stopWatch = Stopwatch.StartNew();
            var fallbackResponse = await SendPayment(client, $"{paymentProcessorsConfig.Fallback["fallback"]}/payments", payment);

            stopWatch.Stop();

            if (fallbackResponse.IsSuccessStatusCode)
            {
                serviceAvaliability.IsServicesOn = true;
                if (stopWatch.ElapsedMilliseconds > 100)
                {
                    serviceAvaliability.IsServiceDefault = true;
                }
                await PersistPayment(dataSource, payment, "Fallback");

                return;
            }

            serviceAvaliability.IsServicesOn = false;
            var paymentReqError = new PaymentClientReq(payment.CorrelationId, payment.Amount);
            await queue.Writer.WriteAsync(paymentReqError);
        }
        else
        {
            var stopWatch = Stopwatch.StartNew();
            var fallbackResponse = await SendPayment(client, $"{paymentProcessorsConfig.Fallback["fallback"]}/payments", payment);

            stopWatch.Stop();

            if (fallbackResponse.IsSuccessStatusCode)
            {
                serviceAvaliability.IsServicesOn = true;
                if (stopWatch.ElapsedMilliseconds > 100)
                {
                    serviceAvaliability.IsServiceDefault = true;
                }
                await PersistPayment(dataSource, payment, "Fallback");

                return;
            }

            serviceAvaliability.IsServicesOn = false;

            var paymentReqError = new PaymentClientReq(payment.CorrelationId, payment.Amount);
            await queue.Writer.WriteAsync(paymentReqError);
        }

    }

    private static async Task<HttpResponseMessage> SendPayment(HttpClient client, string url, PaymentClientProcessed payment)
    {
        StringContent json = new(JsonSerializer.Serialize(payment), Encoding.UTF8, "application/json");

        var response = await client.PostAsync(url, json);

        return response;
    }

    private static async ValueTask PersistPayment(NpgsqlDataSource dataSource, PaymentClientProcessed payment, string service)
    {
        try
        {
            await using var cmd = dataSource.CreateCommand();
            cmd.CommandText = @"INSERT INTO payments (correlationId, amount, requested_at, service)
                                VALUES (@correlationId, @amount, @requested_at, @service);";
            cmd.Parameters.AddWithValue("correlationId", payment.CorrelationId);
            cmd.Parameters.AddWithValue("amount", payment.Amount);
            cmd.Parameters.AddWithValue("requested_at", payment.RequestedAt);
            cmd.Parameters.AddWithValue("service", service);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao persistir pagamento : {ex}");
            return;
        }
    }

}

public class PaymentProcessor(
    QueueManager queue,
    ServiceAvaliability serviceAvaliability,
    HttpClient httpClient,
    int numWorks,
    PaymentProcessorsConfig paymentProcessorsConfig,
    NpgsqlDataSource dataSource) : BackgroundService
{
    private readonly QueueManager _queue = queue;
    private readonly int _numWorks = numWorks;
    private readonly ServiceAvaliability _serviceAvaliability = serviceAvaliability;
    private readonly HttpClient _httpClient = httpClient;
    private readonly PaymentProcessorsConfig _paymentProcessorsConfig = paymentProcessorsConfig;
    private readonly NpgsqlDataSource _dataSource = dataSource;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (int i = 0; i < _numWorks; i++)
        {
            int localWorkerId = i;
            _ = Task.Run(() => Process(stoppingToken, localWorkerId), stoppingToken);
        }

        return Task.CompletedTask;
    }

    private async Task Process(CancellationToken stoppingToken, int workerNumber)
    {
        await foreach (var request in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            var paymentProcessed = new PaymentClientProcessed(request.CorrelationId, request.Amount, DateTime.UtcNow);

            await TryPaymentHandler.ExecuteAsync(paymentProcessed, _httpClient, _serviceAvaliability, _paymentProcessorsConfig, _queue, _dataSource);
        }

    }
}


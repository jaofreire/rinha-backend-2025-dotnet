using api;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

string defaultUrl = Environment.GetEnvironmentVariable("PAYMENT_PROCESSOR_DEFAULT_URL") ?? "http://payment-processor-default:8080";
string fallbackUrl = Environment.GetEnvironmentVariable("PAYMENT_PROCESSOR_FALLBACK_URL") ?? "http://payment-processor-fallback:8080";

var config = new PaymentProcessorsConfig(
    Default: new Dictionary<string, string> { { "default", defaultUrl } },
    Fallback: new Dictionary<string, string> { { "fallback", fallbackUrl } }
);

var dbConnectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING")
    ?? "Host=api-postgres-db;Port=5432;Database=rinha;Username=postgres;Password=postgres";
    

builder.Services.AddSingleton(config);
builder.Services.AddSingleton(new ServiceAvaliability());
builder.Services.AddSingleton<QueueManager>();
builder.Services.AddNpgsqlDataSource(dbConnectionString);
builder.Services.AddHttpClient("client", options =>
{
    options.Timeout = TimeSpan.FromMilliseconds(300);
});

builder.Services.AddHostedService(provider =>
{
    var paymentProcessorsConfig = provider.GetRequiredService<PaymentProcessorsConfig>();
    var serviceAvaliability = provider.GetRequiredService<ServiceAvaliability>();
    var HttpClient = provider.GetRequiredService<HttpClient>();
    
    return new HealthCheck(HttpClient, serviceAvaliability, paymentProcessorsConfig);
});

builder.Services.AddHostedService(provider =>
{
    var queue = provider.GetRequiredService<QueueManager>();
    var logger = provider.GetRequiredService<ILogger<PaymentProcessor>>();
    var serviceAvaliability = provider.GetRequiredService<ServiceAvaliability>();
    var httpClient = provider.GetRequiredService<HttpClient>();
    var paymentProcessorsConfig = provider.GetRequiredService<PaymentProcessorsConfig>();
    var dataSource = provider.GetRequiredService<NpgsqlDataSource>();

    return new PaymentProcessor(queue, serviceAvaliability, httpClient, 5, paymentProcessorsConfig, dataSource);
});


var app = builder.Build();

// Configure the HTTP request pipeline.

app.UseHttpsRedirection();

app.ConfigureEndpoints();

app.Run();

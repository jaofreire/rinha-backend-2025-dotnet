using Microsoft.AspNetCore.Mvc;
using Npgsql;
using NpgsqlTypes;
using System.Buffers;
using System.Text.Json;

namespace api;

public static class Endpoints
{
    public static void ConfigureEndpoints(this WebApplication web)
    {
        web.MapGet("/", () => "Is running...🔥🔥🔥🔥");

        web.MapPost("/payments", async static (HttpContext httpContext, QueueManager queue) =>
        {
            try
            {
                using var memory = MemoryPool<byte>.Shared.Rent(1024);
                var memoryOwner = memory.Memory;

                int totalRead = 0;
                while (true)
                {
                    int read = await httpContext.Request.Body.ReadAsync(memoryOwner[totalRead..]);
                    if (read == 0) break;
                    totalRead += read;
                }

                var span = memoryOwner.Span[..totalRead];
                var reader = new Utf8JsonReader(span);

                Guid correlationId = Guid.Empty;
                decimal amount = 0;

                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        if (reader.ValueTextEquals("correlationId"))
                        {
                            reader.Read();
                            correlationId = reader.GetGuid();
                        }
                        else if (reader.ValueTextEquals("amount"))
                        {
                            reader.Read();
                            amount = reader.GetDecimal();
                        }
                    }
                }

                var request = new PaymentClientReq(correlationId, amount);

                await queue.Writer.WriteAsync(request);
                httpContext.Response.StatusCode = 202;
            }
            catch
            {
                httpContext.Response.StatusCode = 500;
            }
        });

        web.MapGet("/payments-summary", async ([FromQuery] DateTime? from, [FromQuery] DateTime? to, NpgsqlDataSource dataSource) =>
        {
            await using var cmd = dataSource.CreateCommand();
            cmd.CommandText = @"
            SELECT
                COUNT(*)  FILTER (WHERE service = 'Default')  AS default_count,
                COUNT(*)  FILTER (WHERE service = 'Fallback') AS fallback_count,
                SUM(amount) FILTER (WHERE service = 'Default')  AS default_amount_sum,
                SUM(amount) FILTER (WHERE service = 'Fallback') AS fallback_amount_sum
            FROM payments
            WHERE
                (@from IS NULL OR requested_at >= @from) AND
                (@to IS NULL OR requested_at <= @to);
        ";

            cmd.Parameters.Add("@from", NpgsqlDbType.TimestampTz).Value = from.HasValue ? DateTime.SpecifyKind(from.Value, DateTimeKind.Utc) : DBNull.Value;
            cmd.Parameters.Add("@to", NpgsqlDbType.TimestampTz).Value = to.HasValue ? DateTime.SpecifyKind(to.Value, DateTimeKind.Utc) : DBNull.Value;

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var defaultCount = reader.GetInt32(0);
                var fallbackCount = reader.GetInt32(1);
                var defaultAmount = reader.IsDBNull(2) ? 0 : reader.GetDecimal(2);
                var fallbackAmount = reader.IsDBNull(3) ? 0 : reader.GetDecimal(3);

                var result = Results.Ok(new { @default = new
                {
                    totalRequests = defaultCount,
                    totalAmount = defaultAmount
                },
                    fallback = new
                    {
                        totalRequests = fallbackCount,
                        totalAmount = fallbackAmount
                    }
                    });

                return result;
            }
            return Results.Ok();

        });

    }
}
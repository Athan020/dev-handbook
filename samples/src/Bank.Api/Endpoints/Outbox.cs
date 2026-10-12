using System.Text.Json;
using Bank.Api.Data;

namespace Bank.Api.Endpoints;

/// <summary>Builds outbox rows: an event, serialised, with where to publish it.</summary>
public static class Outbox
{
    public static OutboxMessage Message<T>(string exchange, string routingKey, Guid id, T @event) => new()
    {
        Id = id,
        Exchange = exchange,
        RoutingKey = routingKey,
        Type = typeof(T).Name,
        Payload = JsonSerializer.Serialize(@event, JsonSerializerOptions.Web),
        CreatedAt = DateTimeOffset.UtcNow,
    };
}

using System.Text.Json;

namespace InventoryService.api.Messages;

public sealed record CheckoutMessageEnvelope
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Guid MessageId { get; init; }
    public Guid CheckoutId { get; init; }
    public Guid CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public string MessageType { get; init; } = string.Empty;
    public int SchemaVersion { get; init; } = 1;
    public DateTimeOffset SentAtUtc { get; init; }
    public string? ReplyTo { get; init; }
    public JsonElement Payload { get; init; }
}

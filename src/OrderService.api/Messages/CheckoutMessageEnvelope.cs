using System.Text.Json;

namespace OrderService.api.Messages;

public sealed record CheckoutMessageEnvelope
{
    public Guid MessageId { get; set; }
    public Guid CheckoutId { get; set; }
    public Guid CorrelationId { get; set; }
    public Guid? CausationId { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset SentAtUtc { get; set; }
    public string? ReplyTo { get; set; }
    public JsonElement Payload { get; set; }
}

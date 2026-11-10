using BasketService.api.Interfaces;

namespace BasketService.api.Messaging.Envelopes;

public sealed record BasketMessageEnvelope(
    Guid MessageId,
    Guid CheckoutId,
    Guid CorrelationId,
    Guid? CausationId,
    string MessageType,
    int SchemaVersion,
    DateTime SentAtUtc,
    string? ReplyTo,
    string Payload);



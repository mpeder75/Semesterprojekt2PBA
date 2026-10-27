namespace BasketService.api.Messaging.Envelopes;

public sealed record BasketMessageEnvelope(
    string MessageType,
    string Payload);



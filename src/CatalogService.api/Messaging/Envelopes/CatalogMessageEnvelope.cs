namespace CatalogService.api.Messaging.Envelopes;

public sealed record CatalogMessageEnvelope(
    string MessageType,
    string Payload);

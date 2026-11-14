using System.Text.Json.Serialization;

namespace CatalogService.api.Messaging.Envelopes;

/// <summary>
/// Kuverten om en besked (checkout-planens format). Payload kan komme som et JSON-objekt eller som en JSON-tekst
/// (den gamle måde), og ligger her altid som tekst. Felterne til checkouten (CheckoutId osv.) er tomme på gamle beskeder.
/// </summary>
public sealed record CatalogMessageEnvelope(
    Guid MessageId,
    Guid CheckoutId,
    Guid CorrelationId,
    Guid? CausationId,
    string MessageType,
    int SchemaVersion,
    DateTime SentAtUtc,
    string? ReplyTo,
    [property: JsonConverter(typeof(PayloadAsStringConverter))] string Payload);

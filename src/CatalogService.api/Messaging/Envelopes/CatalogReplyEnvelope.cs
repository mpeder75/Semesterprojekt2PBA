using System.Text.Json;
using CatalogService.api.Messaging.Context;

namespace CatalogService.api.Messaging.Envelopes;

/// <summary>
/// Bygger svaret til en checkout-request som en hel kuvert (checkout-planens format). OrderService finder checkouten
/// og det rigtige svar ud fra checkoutId og correlationId, så de skal kopieres fra requesten.
/// </summary>
public static class CatalogReplyEnvelope
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Er requesten en del af en checkout? Så skal svaret sendes som en hel kuvert.</summary>
    public static bool IsCheckoutRequest(CatalogMessageEnvelope request) => request.CheckoutId != Guid.Empty;

    public static string Build(CatalogMessageEnvelope request, CatalogReplyContext replyContext, string replyMessageType, string payloadJson)
    {
        var correlationId = request.CorrelationId != Guid.Empty
            ? request.CorrelationId
            : Guid.TryParse(replyContext.CorrelationId, out var fromProperties) ? fromProperties : Guid.Empty;

        using var payload = JsonDocument.Parse(payloadJson);

        var reply = new
        {
            messageId = Guid.NewGuid(),
            checkoutId = request.CheckoutId,
            correlationId,
            causationId = request.MessageId == Guid.Empty ? (Guid?)null : request.MessageId,
            messageType = replyMessageType,
            schemaVersion = 1,
            sentAtUtc = DateTimeOffset.UtcNow,
            replyTo = (string?)null,
            payload = payload.RootElement
        };

        return JsonSerializer.Serialize(reply, JsonOptions);
    }
}

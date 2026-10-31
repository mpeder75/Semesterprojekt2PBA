namespace BasketService.api.Messaging.Context;

public sealed record BasketReplyContext(
    string? ReplyTo,
    string? CorrelationId);

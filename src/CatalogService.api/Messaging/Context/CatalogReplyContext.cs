namespace CatalogService.api.Messaging.Context;

public sealed record CatalogReplyContext(
    string? ReplyTo,
    string? CorrelationId);

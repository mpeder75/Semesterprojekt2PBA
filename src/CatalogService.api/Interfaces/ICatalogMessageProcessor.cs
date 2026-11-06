using CatalogService.api.Messaging.Context;
using CatalogService.api.Messaging.Envelopes;

namespace CatalogService.api.Interfaces;

public interface ICatalogMessageProcessor
{
    Task ProcessRequestAsync(
        CatalogMessageEnvelope envelope,
        CatalogReplyContext replyContext,
        CancellationToken cancellationToken = default);
}

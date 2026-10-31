using BasketService.api.Messaging.Context;
using BasketService.api.Messaging.Envelopes;

namespace BasketService.api.Interfaces;

public interface IBasketMessageProcessor
{
    Task ProcesMessageAsync(
        BasketMessageEnvelope envelope,
        CancellationToken cancellationToken = default);

    Task ProcessRequestAsync(
        BasketMessageEnvelope envelope,
        BasketReplyContext replyContext,
        CancellationToken ctancellationToken = default);        
}

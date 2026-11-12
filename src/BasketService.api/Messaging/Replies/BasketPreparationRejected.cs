using BasketService.api.Interfaces;

namespace BasketService.api.Messaging.Replies;

public sealed record BasketPreparationRejected(string reason) : IBasketReply<string>;


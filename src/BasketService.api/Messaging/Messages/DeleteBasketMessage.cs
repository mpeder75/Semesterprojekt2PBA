using BasketService.api.Interfaces;

namespace BasketService.api.Messaging.Messages;

public sealed record DeleteBasketMessage(
    int BasketId) : IBasketMessage;

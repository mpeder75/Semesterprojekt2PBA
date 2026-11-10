using BasketService.api.Interfaces;

namespace BasketService.api.Messaging.Messages;

public sealed record PrepareBasketForCheckout(
    int basketId, string buyerId) : IBasketMessage;

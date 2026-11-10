using BasketService.api.Interfaces;

namespace BasketService.api.Messaging.Messages;

public sealed record ReleaseBasketCheckout(int basketId, string buyderId, string reason) : IBasketMessage;

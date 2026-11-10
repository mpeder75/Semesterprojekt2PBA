using BasketService.api.Interfaces;

namespace BasketService.api.Messaging.Messages;

public sealed record CompleteBasketCheckout(int basketId, string buyerId,int orderId) : IBasketMessage;


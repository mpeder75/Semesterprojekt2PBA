using BasketService.api.Interfaces;

namespace BasketService.api.Messaging.Messages;

public sealed record GetBasketByIdMessage(
    int basketId) : IBasketMessage;

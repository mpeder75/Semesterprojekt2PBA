using BasketService.api.Interfaces;

namespace BasketService.api.Messaging.Messages;

public sealed record AddItemToBasketMessage(
    string Username,
    int CatalogItemid,
    decimal price,
    int Quantity = 1) : IBasketMessage;

using BasketService.api.Interfaces;

namespace BasketService.api.Messaging.Messages;

public sealed record SetBasketQuantitiesMessage(
    int BasketId,
    Dictionary<string, int> Quantities) : IBasketMessage;

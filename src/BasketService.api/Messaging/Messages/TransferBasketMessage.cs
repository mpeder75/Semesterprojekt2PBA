using BasketService.api.Interfaces;

namespace BasketService.api.Messaging.Messages;

public sealed record TransferBasketMessage( string AnonmousId, string Username) : IBasketMessage;

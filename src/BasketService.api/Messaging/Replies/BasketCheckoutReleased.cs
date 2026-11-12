using BasketService.api.Entities.DTOs;
using BasketService.api.Interfaces;

namespace BasketService.api.Messaging.Replies;

public sealed record BasketCheckoutReleased(EmptyDto dto) : IBasketReply<EmptyDto>;

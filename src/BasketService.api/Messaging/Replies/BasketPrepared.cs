using BasketService.api.Entities.DTOs;
using BasketService.api.Interfaces;

namespace BasketService.api.Messaging.Replies;

public sealed record BasketPrepared(List<BasketPreparedDTO> reply) : IBasketReply<List<BasketPreparedDTO>>;


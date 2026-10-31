using BasketService.api.Entities.BasketAggregate;
using BasketService.api.Interfaces;

namespace BasketService.api.Messaging.Replies;

public sealed record GetBasketByIdReply( Basket basket) : IBasketReply<Basket>;

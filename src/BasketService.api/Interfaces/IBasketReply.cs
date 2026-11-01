namespace BasketService.api.Interfaces;

public interface IBasketReply : IBasketMessage{
}

public interface IBasketReply<out TResult> : IBasketReply { 
}

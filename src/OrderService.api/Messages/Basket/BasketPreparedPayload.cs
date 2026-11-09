namespace OrderService.api.Messages.Basket;

public class BasketPreparedPayload
{
    public List<BasketLineDto> Items { get; set; } = new();
}

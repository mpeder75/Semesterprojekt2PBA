namespace OrderService.api.Entities.OrderAggregate;

public enum OrderStatus
{
    Confirmed = 0,   // 0 med vilje: eksisterende ordrer bliver Confirmed
    PendingInventory,
    Rejected,
    Cancelled
}

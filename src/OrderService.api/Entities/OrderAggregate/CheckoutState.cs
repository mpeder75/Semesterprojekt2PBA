namespace OrderService.api.Entities.OrderAggregate;

public enum CheckoutState
{
    AwaitingBasket = 0,
    AwaitingCatalog,
    AwaitingStockReservation,
    AwaitingStockConfirmation,
    OrderConfirmedCleanupPending,
    Completed,
    ReleasingStock,
    ReleasingBasket,
    Failed
}

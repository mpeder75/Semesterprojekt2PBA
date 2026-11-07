namespace OrderService.api.Messages;

/// <summary>
/// Kønavnene i checkout-flowet samlet ét sted. Ændres et navn, rettes det kun her.
/// </summary>
public static class CheckoutQueues
{
    // Requests fra Order til de andre services
    public const string Basket = "basket.requests";
    public const string Catalog = "catalog.requests";
    public const string Inventory = "inventory.requests";

    // Replies fra de andre services tilbage til Order
    public const string Replies = "order.checkout.replies";
}

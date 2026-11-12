namespace InventoryService.api.Messages;

internal static class CheckoutQueues
{
    public const string Inventory = "inventory.requests";
    public const string Replies = "order.checkout.replies";
    public const string DeadLetterExchange = "eshop.checkout.dead";
    public const string InventoryDead = "inventory.requests.dead";
}

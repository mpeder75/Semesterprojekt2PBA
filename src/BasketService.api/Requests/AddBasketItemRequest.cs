namespace BasketService.api.Requests;

public sealed record AddBasketItemRequest(
    int CatalogItemId,
    decimal Price,
    int Quantity = 1);

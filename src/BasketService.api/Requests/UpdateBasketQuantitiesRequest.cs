namespace BasketService.api.Requests;

public sealed record UpdateBasketQuantitiesRequest(
    Dictionary<string, int> Quantities);

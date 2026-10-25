namespace BasketService.api.Requests;

public sealed record TransferBasketRequest(
    string AnonymousId,
    string Username);

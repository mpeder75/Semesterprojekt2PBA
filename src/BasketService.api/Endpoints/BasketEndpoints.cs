using BasketService.api.Interfaces;
using BasketService.api.Requests;

namespace BasketService.api.Endpoints;

public static class BasketEndpoints
{
    public static IEndpointRouteBuilder MapBasketEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/basket")
            .WithTags("Basket");

        group.MapPost("/{username}/items", AddItemToBasket)
            .WithName("AddItemToBasket");


        group.MapDelete("/{basketId:int}", DeleteBasket)
            .WithName("DeleteBasket");


        group.MapPut("/{basketId:int}/items/quantities", SetQuantities)
            .WithName("SetBasketQuantities");

        group.MapPost("/transfer", TransferBasket)
            .WithName("TransferBasket");

        return app;
    }

    private static async Task<IResult> AddItemToBasket(
        string username,
        AddBasketItemRequest request,
        IBasketService basketService)
    {
        if (request.Quantity < 0)
        {
            return Results.BadRequest("Quantity cannot be negative.");
        }

        var basket = await basketService.AddItemToBasket(
            username,
            request.CatalogItemId,
            request.Price,
            request.Quantity);

        return Results.Ok(basket);
    }

    private static async Task<IResult> DeleteBasket(
      int basketId,
      IBasketService basketService)
    {
        var deleted = await basketService.DeleteBasketAsync(basketId);

        return deleted ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> SetQuantities(
        int basketId,
        UpdateBasketQuantitiesRequest request,
        IBasketService basketService)
    {
        if (request.Quantities is null
             || request.Quantities.Values.Any(quantity => quantity < 0))
        {
            return Results.BadRequest(
                "Quantities must be provided and values must be zero or greater.");
        }

        var result = await basketService.SetQuantities(
            basketId,
            request.Quantities);

        if (!result.IsSuccess)
        {
            return Results.NotFound();
        }

        return Results.Ok(result.Value);
    }

    private static async Task<IResult> TransferBasket(
        TransferBasketRequest request,
        IBasketService basketService)
    {
        await basketService.TransferBasketAsync(
            request.AnonymousId,
            request.Username);

        return Results.NoContent();
    }
}

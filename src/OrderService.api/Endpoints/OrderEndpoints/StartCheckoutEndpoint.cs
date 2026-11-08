using Microsoft.AspNetCore.Mvc;
using MinimalApi.Endpoint;
using OrderService.api.Entities.OrderAggregate;
using OrderService.api.Interfaces;
using OrderService.api.Services;
using OrderService.api.Specifications;

namespace OrderService.api.Endpoints.OrderEndpoints;

// Bruger det ikke-generiske IEndpoint, fordi IEndpoint<...> kun understøtter op til 3 parametre
public class StartCheckoutEndpoint : IEndpoint
{
    public async Task<IResult> HandleAsync(StartCheckoutRequest request, Guid checkoutId,
        IRepository<CheckoutProcess> checkoutRepository, CheckoutProcessManager manager)
    {
        if (checkoutId == Guid.Empty)
        {
            return Results.BadRequest("Idempotency-Key header is required.");
        }

        if (string.IsNullOrWhiteSpace(request.BuyerId) || request.ShipToAddress is null)
        {
            return Results.BadRequest("BuyerId and ShipToAddress are required.");
        }

        if (request.BasketId <= 0)
        {
            return Results.BadRequest("BasketId is required.");
        }

        var a = request.ShipToAddress;
        var address = new Address(a.Street, a.City, a.State, a.Country, a.ZipCode);

        var existing = await checkoutRepository.FirstOrDefaultAsync(new CheckoutProcessByCheckoutIdSpec(checkoutId));

        if (existing != null)
        {
            // Samme nøgle og samme request er et genforsøg; samme nøgle med andet indhold er en konflikt
            return existing.IsSameRequest(request.BasketId, request.BuyerId, address)
                ? AcceptedResponse(request, existing.CheckoutId)
                : Results.Conflict("Idempotency-Key has already been used with a different request.");
        }

        // En anden nøgle på en kurv, der allerede har en checkout i gang, afvises
        var activeOnBasket = await checkoutRepository.FirstOrDefaultAsync(new ActiveCheckoutByBasketIdSpec(request.BasketId));

        if (activeOnBasket != null)
        {
            return Results.Conflict("The basket already has a checkout in progress.");
        }

        var checkoutProcess = new CheckoutProcess(checkoutId, request.BasketId, request.BuyerId, address);

        await checkoutRepository.AddAsync(checkoutProcess);

        // Tag første trin i flowet (AwaitingBasket -> AwaitingCatalog)
        await manager.AdvanceOrderAsync(checkoutId);

        return AcceptedResponse(request, checkoutProcess.CheckoutId);
    }

    private static IResult AcceptedResponse(StartCheckoutRequest request, Guid checkoutId)
    {
        var response = new StartCheckoutResponse(request.CorrelationId())
        {
            CheckoutId = checkoutId,
            StatusUrl = $"/api/orders/checkouts/{checkoutId}"
        };

        return Results.Accepted(response.StatusUrl, response);
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/checkouts",
                async (StartCheckoutRequest request,
                    [FromHeader(Name = "Idempotency-Key")] Guid checkoutId,
                    IRepository<CheckoutProcess> checkoutRepository,
                    CheckoutProcessManager manager) =>
                {
                    return await HandleAsync(request, checkoutId, checkoutRepository, manager);
                })
            .Produces<StartCheckoutResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }
}

using MinimalApi.Endpoint;
using OrderService.api.Entities.OrderAggregate;
using OrderService.api.Interfaces;
using OrderService.api.Specifications;

namespace OrderService.api.Endpoints.OrderEndpoints;

public class GetCheckoutStatusEndpoint : IEndpoint<IResult, Guid, IRepository<CheckoutProcess>>
{
    public async Task<IResult> HandleAsync(Guid checkoutId, IRepository<CheckoutProcess> checkoutRepository)
    {
        if (checkoutId == Guid.Empty)
        {
            return Results.BadRequest("CheckoutId is required.");
        }

        var checkout = await checkoutRepository.FirstOrDefaultAsync(new CheckoutProcessByCheckoutIdSpec(checkoutId));

        if (checkout is null)
        {
            return Results.NotFound();
        }

        var response = new CheckoutStatusResponse
        {
            CheckoutId = checkout.CheckoutId,
            State = checkout.State.ToString(),
            CreatedAt = checkout.CreatedAt
        };

        return Results.Ok(response);
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/checkouts/{checkoutId}",
                async (Guid checkoutId, IRepository<CheckoutProcess> repo) =>
                    await HandleAsync(checkoutId, repo))
            .Produces<CheckoutStatusResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }
}

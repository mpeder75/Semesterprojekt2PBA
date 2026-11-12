using MinimalApi.Endpoint;
using OrderService.api.Entities.OrderAggregate;
using OrderService.api.Interfaces;
using OrderService.api.Specifications;

namespace OrderService.api.Endpoints.OrderEndpoints;

public class GetCheckoutStatusEndpoint : IEndpoint<IResult, Guid, IRepository<CheckoutProcess>, IRepository<Order>>
{
    public async Task<IResult> HandleAsync(Guid checkoutId, IRepository<CheckoutProcess> checkoutRepository, IRepository<Order> orderRepository)
    {
        if (checkoutId == Guid.Empty) {
            return Results.BadRequest("CheckoutId is required.");
        }

        var checkout = await checkoutRepository.FirstOrDefaultAsync(new CheckoutProcessByCheckoutIdSpec(checkoutId));

        if (checkout is null)
        {
            return Results.NotFound();
        }

        var order = await orderRepository.FirstOrDefaultAsync(new OrderByCheckoutIdSpec(checkoutId));

        var response = new CheckoutStatusResponse
        {
            CheckoutId = checkout.CheckoutId,
            OrderId = order?.Id,
            State = checkout.State.ToString(),
            Status = TranslateState(checkout.State),
            RecoveryStatus = checkout.RecoveryStatus.ToString(),
            CreatedAt = checkout.CreatedAt,
            FailureReason = checkout.FailureReason
        };

        return Results.Ok(response);
    }

    private static string TranslateState(CheckoutState state)
    {
        var result = state switch
        {
           CheckoutState.Completed => "Completed",
           CheckoutState.Failed => "Failed",
           _ => "Processing"
        };
        return result;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/checkouts/{checkoutId}",
                async (Guid checkoutId, IRepository<CheckoutProcess> repo, IRepository<Order> orderRepo) =>
                    await HandleAsync(checkoutId, repo, orderRepo))
            .Produces<CheckoutStatusResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }
}

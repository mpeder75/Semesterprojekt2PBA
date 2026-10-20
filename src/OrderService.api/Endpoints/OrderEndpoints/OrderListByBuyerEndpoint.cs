using MinimalApi.Endpoint;
using OrderService.api.Entities.OrderAggregate;
using OrderService.api.Interfaces;
using OrderService.api.Specifications;

namespace OrderService.api.Endpoints.OrderEndpoints;

/// <summary>
/// List the Orders of a buyer
/// </summary>
public class OrderListByBuyerEndpoint : IEndpoint<IResult, ListOrdersByBuyerRequest, IReadRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders",
            async (string buyerId, IReadRepository<Order> orderRepository) =>
            {
                return await HandleAsync(new ListOrdersByBuyerRequest(buyerId), orderRepository);
            })
            .Produces<ListOrdersByBuyerResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListOrdersByBuyerRequest request, IReadRepository<Order> orderRepository)
    {
        var response = new ListOrdersByBuyerResponse(request.CorrelationId());

        var orders = await orderRepository.ListAsync(new CustomerOrdersSpecification(request.BuyerId));

        response.Orders.AddRange(orders.Select(order => new OrderSummaryDto
        {
            Id = order.Id,
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Address = new AddressDto
            {
                Street = order.ShipToAddress.Street,
                City = order.ShipToAddress.City,
                State = order.ShipToAddress.State,
                Country = order.ShipToAddress.Country,
                ZipCode = order.ShipToAddress.ZipCode
            }
        }));

        return Results.Ok(response);
    }
}

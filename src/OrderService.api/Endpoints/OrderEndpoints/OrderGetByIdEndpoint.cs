using MinimalApi.Endpoint;
using OrderService.api.Entities.OrderAggregate;
using OrderService.api.Interfaces;
using OrderService.api.Specifications;

namespace OrderService.api.Endpoints.OrderEndpoints;

/// <summary>
/// Get an Order (with items) by Id
/// </summary>
public class OrderGetByIdEndpoint : IEndpoint<IResult, GetByIdOrderRequest, IReadRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}",
            async (int orderId, IReadRepository<Order> orderRepository) =>
            {
                return await HandleAsync(new GetByIdOrderRequest(orderId), orderRepository);
            })
            .Produces<GetByIdOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(GetByIdOrderRequest request, IReadRepository<Order> orderRepository)
    {
        var response = new GetByIdOrderResponse(request.CorrelationId());

        var order = await orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(request.OrderId));
        if (order is null)
            return Results.NotFound();

        response.Order = new OrderDetailDto
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
            },
            OrderItems = order.OrderItems.Select(oi => new OrderItemDto
            {
                ProductId = oi.ItemOrdered.CatalogItemId,
                ProductName = oi.ItemOrdered.ProductName,
                PictureUrl = oi.ItemOrdered.PictureUri,
                UnitPrice = oi.UnitPrice,
                Units = oi.Units
            }).ToList()
        };

        return Results.Ok(response);
    }
}

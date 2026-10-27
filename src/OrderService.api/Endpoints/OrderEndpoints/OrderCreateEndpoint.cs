using MinimalApi.Endpoint;
using OrderService.api.Entities.OrderAggregate;
using OrderService.api.Interfaces;

namespace OrderService.api.Endpoints.OrderEndpoints;

public class OrderCreateEndpoint : IEndpoint<IResult, CreateOrderRequest, IRepository<Order>>
{
    public async Task<IResult> HandleAsync(CreateOrderRequest request, IRepository<Order> orderRepository)
    {
        if (string.IsNullOrWhiteSpace(request.BuyerId)
      || request.ShipToAddress is null
      || request.Items is null
      || request.Items.Count == 0)
        {
            return Results.BadRequest(
                "A buyer, shipping address and at least one order item are required.");
        }

        if (request.Items.Any(item =>
            item is null || item.Units <= 0 || item.UnitPrice < 0))
        {
            return Results.BadRequest(
                "Order items must have positive units and non-negative prices.");
        }

        var a = request.ShipToAddress;
      var address = new Address(a.Street, a.City, a.State,a.Country, a.ZipCode);

      var items = request.Items.Select(dto =>
      {
          var itemOrdered = new CatalogItemOrdered(dto.CatalogItemId, dto.ProductName, dto.PictureUrl);
          return new OrderItem(itemOrdered, dto.UnitPrice, dto.Units);
      }).ToList();

      var order = new Order(request.BuyerId, address, items);

      await orderRepository.AddAsync(order);

      var response = new CreateOrderResponse(request.CorrelationId());
      response.OrderId = order.Id;

      return Results.Created($"/api/orders/{order.Id}", response);
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            async (CreateOrderRequest request, IRepository<Order> orderRepository) =>
            {
                return await HandleAsync(request, orderRepository);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }
}

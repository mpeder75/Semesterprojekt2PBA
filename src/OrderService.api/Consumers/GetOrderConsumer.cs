using System.Text;
using System.Text.Json;
using OrderService.api.Entities.OrderAggregate;
using OrderService.api.Interfaces;
using OrderService.api.Messages;
using OrderService.api.Specifications;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OrderService.api.Consumers;

public class GetOrderConsumer : BackgroundService
{
    private const string QueueName = "order.get";
    private readonly IConnection _connection;
    private readonly ILogger<GetOrderConsumer> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public GetOrderConsumer(ILogger<GetOrderConsumer> logger, IConnection connection, IServiceScopeFactory scopeFactory)
    {
        _connection = connection;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await channel.QueueDeclareAsync(
            QueueName,
            true,
            false,
            false,
            null,
            cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);

        consumer.ReceivedAsync += async (_, ea) =>
        {
            try
            {
                var body = Encoding.UTF8.GetString(ea.Body.ToArray());

                var request = JsonSerializer.Deserialize<GetOrderRequest>(body)
                              ?? throw new InvalidOperationException("Empty order list.");

                if (request.OrderId <= 0)
                {
                    throw new InvalidOperationException("OrderId ID is required.");
                }

                var replyTo = ea.BasicProperties.ReplyTo;
                var correlationId = ea.BasicProperties.CorrelationId;

                if (string.IsNullOrWhiteSpace(replyTo) || string.IsNullOrWhiteSpace(correlationId))
                    throw new InvalidOperationException("ReplyTo and CorrelationId are required.");

                await using var scope = _scopeFactory.CreateAsyncScope();

                var orderRepository = scope.ServiceProvider.GetRequiredService<IReadRepository<Order>>();
                var order = await orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(request.OrderId), stoppingToken);

                var getOrderResponse = new GetOrderResponse();

                if (order != null)
                {
                    getOrderResponse.Found = true;
                    getOrderResponse.Id = order.Id;
                    getOrderResponse.OrderDate = order.OrderDate;
                    getOrderResponse.Total = order.Total();
                    getOrderResponse.Address = new AddressMessage
                    {
                        Street = order.ShipToAddress.Street,
                        City = order.ShipToAddress.City,
                        State = order.ShipToAddress.State,
                        Country = order.ShipToAddress.Country,
                        ZipCode = order.ShipToAddress.ZipCode
                    };
                    getOrderResponse.Items = order.OrderItems.Select(oi => new OrderItemMessage
                    {
                        ProductId = oi.ItemOrdered.CatalogItemId,
                        ProductName = oi.ItemOrdered.ProductName,
                        PictureUrl = oi.ItemOrdered.PictureUri,
                        UnitPrice = oi.UnitPrice,
                        Units = oi.Units
                    }).ToList();
                }

                var responseBody = JsonSerializer.SerializeToUtf8Bytes(getOrderResponse);
                var props = new BasicProperties { CorrelationId = correlationId };

                await channel.BasicPublishAsync(exchange: string.Empty, routingKey: replyTo, mandatory: false,
                    basicProperties: props, body: responseBody, cancellationToken: stoppingToken);

                _logger.LogInformation("Order {OrderId} found: {Found}", request.OrderId, getOrderResponse.Found);

                await channel.BasicAckAsync(ea.DeliveryTag, false, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process message. Delivery tag: {DeliveryTag}", ea.DeliveryTag);

                await channel.BasicNackAsync(ea.DeliveryTag, false, false,
                    stoppingToken);
            }
        };

        await channel.BasicConsumeAsync(
            QueueName,
            false,
            consumer,
            stoppingToken);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}


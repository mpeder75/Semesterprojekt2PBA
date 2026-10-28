using System.Text;
using System.Text.Json;
using OrderService.api.Entities.OrderAggregate;
using OrderService.api.Interfaces;
using OrderService.api.Messages;
using OrderService.api.Specifications;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OrderService.api.Consumers;

public class ListOrdersConsumer : BackgroundService
{
    private const string QueueName = "order.list";
    private readonly IConnection _connection;
    private readonly ILogger<ListOrdersConsumer> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public ListOrdersConsumer(ILogger<ListOrdersConsumer> logger, IConnection connection,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _connection = connection;
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

                var request = JsonSerializer.Deserialize<ListOrdersRequest>(body)
                              ?? throw new InvalidOperationException("Empty order list.");

                if (string.IsNullOrWhiteSpace(request.BuyerId))
                {
                    throw new InvalidOperationException("Buyer ID is required.");
                }

                await using var scope = _scopeFactory.CreateAsyncScope();

                var orderRepository = scope.ServiceProvider.GetRequiredService<IReadRepository<Order>>();
                var orders = await orderRepository.ListAsync(new CustomerOrdersSpecification(request.BuyerId), stoppingToken);

                var listOrdersResponse = new ListOrdersResponse();

                listOrdersResponse.Orders.AddRange(orders.Select(order => new OrderSummaryMessage
                {
                    Id = order.Id,
                    OrderDate = order.OrderDate,
                    Total = order.Total(),
                    Address = new AddressMessage
                    {
                        Street = order.ShipToAddress.Street,
                        City = order.ShipToAddress.City,
                        State = order.ShipToAddress.State,
                        Country = order.ShipToAddress.Country,
                        ZipCode = order.ShipToAddress.ZipCode
                    }
                }));

                var replyTo = ea.BasicProperties.ReplyTo;
                var correlationId = ea.BasicProperties.CorrelationId;

                if (string.IsNullOrWhiteSpace(replyTo) || string.IsNullOrWhiteSpace(correlationId))
                    throw new InvalidOperationException("ReplyTo and CorrelationId are required.");

                var responseBody = JsonSerializer.SerializeToUtf8Bytes(listOrdersResponse);

                var props = new BasicProperties { CorrelationId = correlationId };

                await channel.BasicPublishAsync(exchange: string.Empty, routingKey: replyTo, mandatory: false,
                    basicProperties: props, body: responseBody, cancellationToken: stoppingToken);


                _logger.LogInformation("Found {Count} orders for buyer: {BuyerId}", listOrdersResponse.Orders.Count, request.BuyerId);

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

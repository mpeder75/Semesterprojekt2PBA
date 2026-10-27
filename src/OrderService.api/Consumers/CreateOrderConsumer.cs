using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text.Json;
using OrderService.api.Entities.OrderAggregate;
using OrderService.api.Interfaces;
using OrderService.api.Messages;

namespace OrderService.api.Consumers;

public class CreateOrderConsumer : BackgroundService
{
    private const string QueueName = "order.create";
    private readonly ILogger<CreateOrderConsumer> _logger;
    private readonly IConnection _connection;
    private readonly IServiceScopeFactory _scopeFactory;

    public CreateOrderConsumer(ILogger<CreateOrderConsumer> logger, IConnection connection, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _connection = connection;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);

        consumer.ReceivedAsync += async (_, ea) =>
        {
            try
            {
                var body = Encoding.UTF8.GetString(ea.Body.ToArray());

                var command = JsonSerializer.Deserialize<CreateOrderCommand>(body)
                    ?? throw new InvalidOperationException("Empty order command.");

                await using var scope = _scopeFactory.CreateAsyncScope();
                var orderRepository = scope.ServiceProvider.GetRequiredService<IRepository<Order>>();

                var a = command.ShipToAddress;
                var address = new Address(a.Street, a.City, a.State, a.Country, a.ZipCode);

                var items = command.Items.Select(i =>
                {
                    var itemOrdered = new CatalogItemOrdered(i.CatalogItemId, i.ProductName, i.PictureUrl);
                    return new OrderItem(itemOrdered, i.UnitPrice, i.Units);
                }).ToList();

                var order = new Order(command.BuyerId, address, items);

                await orderRepository.AddAsync(order);


                _logger.LogInformation("Received a message: {BuyerId}", command.BuyerId);

                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process message. Delivery tag: {DeliveryTag}", ea.DeliveryTag);

                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: stoppingToken);
            }
        };

        await channel.BasicConsumeAsync(
            queue: QueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}

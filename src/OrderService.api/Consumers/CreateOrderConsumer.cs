using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text.Json;
using OrderService.api.Messages;

namespace OrderService.api.Consumers;

public class CreateOrderConsumer : BackgroundService
{
    private const string QueueName = "order.create";
    private readonly ILogger<CreateOrderConsumer> _logger;
    private readonly IConnection _connection;

    public CreateOrderConsumer(ILogger<CreateOrderConsumer> logger, IConnection connection)
    {
        _logger = logger;
        _connection = connection;
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

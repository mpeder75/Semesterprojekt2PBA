using System.Text;
using System.Text.Json;
using OrderService.api.Messages;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OrderService.api.Consumers;

public class OrderCheckoutReplyConsumer : BackgroundService
{
    private const string QueueName = CheckoutQueues.Replies;
    private readonly ILogger<OrderCheckoutReplyConsumer> _logger;
    private readonly IConnection _connection;
    private readonly IServiceScopeFactory _scopeFactory;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public OrderCheckoutReplyConsumer(ILogger<OrderCheckoutReplyConsumer> logger, IConnection connection, IServiceScopeFactory scopeFactory)
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

                var envelope = JsonSerializer.Deserialize<CheckoutMessageEnvelope>(body, JsonOptions)
                              ?? throw new InvalidOperationException("Empty reply message");

                _logger.LogInformation("Reply received: {MessageType} for checkout {CheckoutId}", envelope.MessageType, envelope.CheckoutId);

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

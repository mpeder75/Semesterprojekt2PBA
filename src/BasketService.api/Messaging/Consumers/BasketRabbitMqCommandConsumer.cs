using System.Text.Json;
using BasketService.api.Messaging.Deserialization.Resolver;
using BasketService.api.Messaging.Envelopes;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace BasketService.api.Messaging.Consumers;

public class BasketRabbitMqCommandConsumer : BackgroundService
{
    private const string QueueName = "basket.commands";
    IConnection _connection;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<BasketRabbitMqCommandConsumer> _logger;
    private readonly BasketMessageDeserializerResolver _messageDeserializerResolver;

    public BasketRabbitMqCommandConsumer(
        IConnection connection, 
        IServiceScopeFactory serviceScopeFactory,
        ILogger<BasketRabbitMqCommandConsumer> logger,
        BasketMessageDeserializerResolver messageDeserializerResolver)
    {
        _connection = connection;
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
        _messageDeserializerResolver = messageDeserializerResolver;
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

        await channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: 1,
            global: false,
            cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);

        consumer.ReceivedAsync += async (_, ea) =>
        {
            try
            {
                var envolope = JsonSerializer.Deserialize<BasketMessageEnvelope>(ea.Body.ToArray())
                ??
                throw new InvalidOperationException("Empty basket message.");

                var message = _messageDeserializerResolver.Deserialize(envolope);

                await using var scope = _serviceScopeFactory.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<BasketMessageDispatcher>();
                await dispatcher.DispatchMessageAsync(message, stoppingToken);
                await channel.BasicAckAsync(
                    ea.DeliveryTag,
                    multiple:false,
                    cancellationToken: stoppingToken);
            }
            catch(OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process basket message. Delivery tag: {DeliveryTag}", ea.DeliveryTag);

                await channel.BasicNackAsync(
                    ea.DeliveryTag,
                    multiple: false,
                    requeue: false,
                    cancellationToken: stoppingToken);
            }
        };

        await channel.BasicConsumeAsync(
            queue: QueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}

using System.Text;
using System.Text.Json;
using BasketService.api.Interfaces;
using BasketService.api.Messaging.Context;
using BasketService.api.Messaging.Deserialization.Resolver;
using BasketService.api.Messaging.Envelopes;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace BasketService.api.Messaging.Consumers;

public sealed class BasketRabbitMqRequestConsumer : BackgroundService
{
    private readonly IConnection _connection;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<BasketRabbitMqRequestConsumer> _logger;
    private IChannel? _channel;

    public BasketRabbitMqRequestConsumer(IConnection connection, IServiceScopeFactory serviceScopeFactory, ILogger<BasketRabbitMqRequestConsumer> logger)
    {
        _connection = connection;
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _channel = await _connection.CreateChannelAsync(
            cancellationToken: stoppingToken);

        await _channel.BasicQosAsync(
            prefetchCount: 1,
            prefetchSize: 0,
            global: false,
            cancellationToken: stoppingToken);

        await _channel.QueueDeclareAsync(
            queue: "basket.requests",
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);

        consumer.ReceivedAsync += async (_, eventArgs) =>
        {
            try
            {
                var payload = Encoding.UTF8.GetString(eventArgs.Body.ToArray());

                var envelope = JsonSerializer.Deserialize<BasketMessageEnvelope>(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ??
                throw new JsonException("Failed to deserialize BasketMessageEnvelope.");

                var replyContext = new BasketReplyContext(
                    eventArgs.BasicProperties.ReplyTo,
                    eventArgs.BasicProperties.CorrelationId);

                await using var scope = _serviceScopeFactory.CreateAsyncScope();

                var proccessor = scope.ServiceProvider.GetRequiredService<IBasketMessageProcessor>();

                await proccessor.ProcessRequestAsync(envelope, replyContext, stoppingToken);

                await _channel.BasicAckAsync(
                    eventArgs.DeliveryTag,
                    multiple: false,
                    cancellationToken: stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process RabbitMQ request");

                await _channel.BasicNackAsync(
                    eventArgs.DeliveryTag,
                    multiple: false,
                    requeue: false,
                    cancellationToken: stoppingToken);
            }
        };

        await _channel.BasicConsumeAsync(
            queue: "basket.requests",
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation("Basket RabbitMQ request consumer started.");

        await Task.Delay(
            Timeout.Infinite,
            stoppingToken);
    }

    public override async Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Stopping Basket RabbitMQ request consumer.");

        if(_channel is not null)
        {
            await _channel.CloseAsync(stoppingToken);
            await _channel.DisposeAsync();
        }

        await base.StopAsync(stoppingToken);
    }
}

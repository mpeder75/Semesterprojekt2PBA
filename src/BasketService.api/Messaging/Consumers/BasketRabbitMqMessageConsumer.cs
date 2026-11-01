
using System.Text;
using System.Text.Json;
using BasketService.api.Interfaces;
using BasketService.api.Messaging.Envelopes;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace BasketService.api.Messaging.Consumers;

public sealed class BasketRabbitMqMessageConsumer : BackgroundService
{
    private readonly IConnection _connection;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<BasketRabbitMqMessageConsumer> _logger;

    private IChannel? _channel;

    public BasketRabbitMqMessageConsumer(
        IConnection connection,
        IServiceScopeFactory serviceScopeFactory,
        ILogger<BasketRabbitMqMessageConsumer> logger)
    {
        _connection = connection;
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _channel = await _connection.CreateChannelAsync(
            cancellationToken: stoppingToken);

        await _channel.BasicQosAsync(
            prefetchCount: 1,
            prefetchSize: 0,
            global: false,
            cancellationToken: stoppingToken);

        await _channel.QueueDeclareAsync(
            queue: "basket.messages",
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
                var payload = Encoding.UTF8.GetString(
                    eventArgs.Body.ToArray());

                var envelope = JsonSerializer.Deserialize<BasketMessageEnvelope>(
                    payload,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))
                    ?? throw new JsonException(
                        "Failed to deserialize BasketMessageEnvelope.");

                await using var scope =
                    _serviceScopeFactory.CreateAsyncScope();

                var processor = scope.ServiceProvider
                    .GetRequiredService<IBasketMessageProcessor>();

                await processor.ProcesMessageAsync(
                    envelope,
                    stoppingToken);

                await _channel.BasicAckAsync(
                    eventArgs.DeliveryTag,
                    multiple: false,
                    cancellationToken: stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to process RabbitMQ message");

                await _channel.BasicNackAsync(
                    eventArgs.DeliveryTag,
                    multiple: false,
                    requeue: false,
                    cancellationToken: stoppingToken);
            }
        };

        await _channel.BasicConsumeAsync(
            queue: "basket.commands",
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation(
            "Basket RabbitMQ message consumer started.");

        await Task.Delay(
            Timeout.Infinite,
            stoppingToken);
    }

    public override async Task StopAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Stopping Basket RabbitMQ message consumer.");

        if (_channel is not null)
        {
            await _channel.CloseAsync(stoppingToken);
            await _channel.DisposeAsync();
        }

        await base.StopAsync(stoppingToken);
    }
}

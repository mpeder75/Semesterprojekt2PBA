using System.Text.Json;
using BasketService.api.Messaging.Deserialization.Resolver;
using BasketService.api.Messaging.Envelopes;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace BasketService.api.Messaging.Consumers;

public sealed class BasketRabbitMqRequestConsumer : BackgroundService
{
    private const string queueName = "basket.requests";

    private readonly IConnection _connection;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<BasketRabbitMqRequestConsumer> _logger;
    private readonly BasketMessageDeserializerResolver _messageDeserializerResolver;

    public BasketRabbitMqRequestConsumer(
        IConnection connection, 
        IServiceScopeFactory serviceScopeFactory, 
        ILogger<BasketRabbitMqRequestConsumer> logger,
        BasketMessageDeserializerResolver MessageDeserializerResolver)
    {
        _connection = connection;
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
        _messageDeserializerResolver = MessageDeserializerResolver;
    }

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        await using var channel = await _connection.CreateChannelAsync(cancellationToken: token);

        await channel.QueueDeclareAsync(
            queue: queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: token);

        await channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: 1,
            global: false,
            cancellationToken: token);

        var consumer = new AsyncEventingBasicConsumer(channel);

        consumer.ReceivedAsync += async (_, ea) =>
        {
            try
            {
                var replyTo = ea.BasicProperties.ReplyTo;

                if (string.IsNullOrWhiteSpace(replyTo))
                {
                    throw new InvalidOperationException("Request message must specify a ReplyTo");
                }

                var envelope = JsonSerializer.Deserialize<BasketMessageEnvelope>(ea.Body.ToArray())
                ??
                throw new InvalidOperationException("Empty basket reuqest message");

                var message = _messageDeserializerResolver.Deserialize(envelope);

                object? reply;

                await using (var scope = _serviceScopeFactory.CreateAsyncScope())
                {
                    var dispatcher = scope.ServiceProvider.GetRequiredService<BasketMessageDispatcher>();

                    reply = await dispatcher.DispatchReplyAsync(message, token);
                }

                var responseBody = JsonSerializer.SerializeToUtf8Bytes(reply, reply?.GetType() ?? typeof(object));

                var responseProperties = new BasicProperties
                {
                    CorrelationId = ea.BasicProperties.CorrelationId,
                    ContentType = "applicaiton/json"
                };

                //TODO: Define exchange
                await channel.BasicPublishAsync(
                    exchange: "",
                    routingKey: replyTo,
                    mandatory: true,
                    basicProperties: responseProperties,
                    body: responseBody,
                    cancellationToken: token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process basket request. Delivery tag: {DeliveryTag}", ea.DeliveryTag);

                await channel.BasicNackAsync(
                    ea.DeliveryTag,
                    multiple: false,
                    requeue: false,
                    cancellationToken: token);
            }
        };
    }
}

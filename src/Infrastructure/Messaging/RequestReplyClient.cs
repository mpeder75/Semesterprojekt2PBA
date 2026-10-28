using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class RequestReplyClient : IRequestReplyClient
{
    private readonly IConnection _connection;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<byte[]>> _pendingRequests;
    IChannel? _channel;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private string? _replyQueueName;

    public RequestReplyClient(IConnection connection)
    {
        _connection = connection;
        _pendingRequests = new ConcurrentDictionary<string, TaskCompletionSource<byte[]>>();
    }


    public async Task<TResponse> RequestAsync<TRequest, TResponse>(string queueName, TRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var correlationId = Guid.NewGuid().ToString();

        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[correlationId] = tcs;

        var body = JsonSerializer.SerializeToUtf8Bytes(request);

        var props = new BasicProperties
        {
            CorrelationId = correlationId,
            ReplyTo = _replyQueueName
        };

        await _channel!.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: queueName,
            mandatory: false,
            basicProperties: props,
            body: body,
            cancellationToken: cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));

        byte[] replyBytes;
        try
        {
            replyBytes = await tcs.Task.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            _pendingRequests.TryRemove(correlationId, out _);
            throw new TimeoutException($"No reply from '{queueName}' within 5 seconds.");
        }

        return JsonSerializer.Deserialize<TResponse>(replyBytes)
               ?? throw new InvalidOperationException("Empty reply.");
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_channel != null)
        {
            return;
        }

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_channel != null)
            {
                return;
            }

            _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

            // 3. svar-kø
            var replyQueue = await _channel.QueueDeclareAsync(cancellationToken: cancellationToken);
            _replyQueueName = replyQueue.QueueName;

            // 4. consumer
            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.ReceivedAsync += (_, ea) =>
            {
                var correlationId = ea.BasicProperties.CorrelationId;
                if (correlationId != null && _pendingRequests.TryRemove(correlationId, out var tcs))
                {
                    tcs.TrySetResult(ea.Body.ToArray());
                }
                return Task.CompletedTask;
            };

            // 5. BasicConsume
            await _channel.BasicConsumeAsync(_replyQueueName, autoAck: true, consumer, cancellationToken);
        }
        finally
        {
            _initLock.Release();
        }
    }
}

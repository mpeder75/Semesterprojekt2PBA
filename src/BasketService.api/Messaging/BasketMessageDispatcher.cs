using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using BasketService.api.Interfaces;

namespace BasketService.api.Messaging;

public sealed class BasketMessageDispatcher : IBasketMessageDispatcher
{
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public BasketMessageDispatcher(IServiceScopeFactory serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
    }

    public async Task DispatchMessageAsync(IBasketMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        await using var scope = _serviceScopeFactory.CreateAsyncScope();

        var handlerType = typeof(IBasketMessageHandler<>).MakeGenericType(message.GetType());

        var handler = scope.ServiceProvider.GetRequiredService(handlerType);

        var method = handlerType.GetMethod("HandleAsync")!;

        var task = (Task)method.Invoke(handler, new object[] { message, cancellationToken })!;

        await task;

    }

    public async Task<IBasketReply> DispatchRequestAsync(IBasketMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        await using var scope = _serviceScopeFactory.CreateAsyncScope();

        var handlerInterface = scope.ServiceProvider
            .GetServices<IBasketMessageHandlerAdapter>()
            .SingleOrDefault(x => x.MessageType == message.GetType());

        if (handlerInterface is null)
        {
            throw new InvalidOperationException($"No reply for handler registered for {message.GetType().Name}");
        }

        var reply = await handlerInterface.HandleAsync(
            scope.ServiceProvider,
            message,
            cancellationToken);

        return reply ?? throw new InvalidOperationException($"Request handler for {message.GetType().Name} returend a null reply");
    }
}

using CatalogService.api.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace CatalogService.api.Messaging.Dispatcher;

public class CatalogMessageDispatcher : ICatalogMessageDispatcher
{
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public CatalogMessageDispatcher(IServiceScopeFactory serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
    }
    public async Task<ICatalogReply> DispatchRequestAsync(ICatalogMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        await using var scope = _serviceScopeFactory.CreateAsyncScope();

        var handlerInterface = scope.ServiceProvider
            .GetServices<ICatalogReplyHandlerAdapter>()
            .SingleOrDefault(x => x.MessageType == message.GetType());

        if (handlerInterface is null)
        {
            throw new InvalidOperationException($"No replay handler registered for {message.GetType().Name}.");
        }

        var reply = await handlerInterface.HandleAsync(
            scope.ServiceProvider,
            message,
            cancellationToken);

        return reply ?? throw new InvalidOperationException($"Request hander for {message.GetType().Name} returend a null reply.");
    }
}

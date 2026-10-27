using BasketService.api.Interfaces;

namespace BasketService.api.Messaging;

public sealed class BasketMessageDispatcher
{
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public BasketMessageDispatcher(IServiceScopeFactory serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
    }

    public async Task DispatchAsync( IBasketMessage message, CancellationToken cancellationToken = default)
    {
        await using var scope = _serviceScopeFactory.CreateAsyncScope();

        var handlerType = typeof(IBasketMessageHandler<>).MakeGenericType(message.GetType());

        var handler = scope.ServiceProvider.GetRequiredService(handlerType);

        var method = handlerType.GetMethod(nameof(IBasketMessageHandler<IBasketMessage>.HandleAsync))!;

        var task = (Task)method.Invoke(handler, new object[] { message, cancellationToken })!;

        await task;
    }
}

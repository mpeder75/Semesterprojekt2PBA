using BasketService.api.Interfaces;

namespace BasketService.api.Messaging;

public sealed class BasketMessageDispatcher
{
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public BasketMessageDispatcher(IServiceScopeFactory serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
    }

    //For messages that don't need a reply.
    public async Task DispatchMessageAsync( IBasketMessage message, CancellationToken cancellationToken = default)
    {
        await using var scope = _serviceScopeFactory.CreateAsyncScope();

        var handlerType = typeof(IBasketMessageHandler<>).MakeGenericType(message.GetType());

        var handler = scope.ServiceProvider.GetRequiredService(handlerType);

        var method = handlerType.GetMethod(nameof(IBasketMessageHandler<IBasketMessage>.HandleAsync))!;

        var task = (Task)method.Invoke(handler, new object[] { message, cancellationToken })!;

        await task;
    }

    //For messages that need a reply
    public async Task<TResult> DispatchReplyAsync<TResult>(IBasketMessage message, CancellationToken cancellationToken = default)
    {
        await using var scope = _serviceScopeFactory.CreateAsyncScope();

        var handlerType = typeof(IBasketMessageHandler<,>).MakeGenericType(message.GetType(), typeof(TResult));

        var handler = scope.ServiceProvider.GetRequiredService(handlerType);

        var method = handlerType.GetMethod(nameof(IBasketMessageHandler<IBasketMessage>.HandleAsync))!;

        var task = (Task<TResult>)method.Invoke(handler, new object[] { message, cancellationToken })!;

        return await task;
    }

    public async Task<object?> DispatchReplyAsync(IBasketMessage message, CancellationToken cancellationToken)
    {
        var handleInterFace = typeof(IBasketMessageHandler<,>);

        var handleType = AppDomain
            .CurrentDomain
            .GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .FirstOrDefault(i =>
                i.IsGenericType 
                && i.GetGenericTypeDefinition() == handleInterFace
                && i.GetGenericArguments()[0] == message.GetType());

        if (handleType is null)
        {
            throw new InvalidOperationException($"No result handler registered for {message.GetType().Name}");
        }

        var resultType = handleType.GetGenericArguments()[1];

        await using var scope = _serviceScopeFactory.CreateAsyncScope();

        var handler = scope.ServiceProvider.GetRequiredService(handleType);

        var method = handleType.GetMethod("HandleAsync")!;

        var task = (Task)method.Invoke(handler, new object[] { message, cancellationToken })!;

        await task;

        return task.GetType().GetProperty("Rsult")!.GetValue(task);
    }
}

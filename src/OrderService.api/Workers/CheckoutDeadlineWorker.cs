using OrderService.api.Entities.OrderAggregate;
using OrderService.api.Interfaces;
using OrderService.api.Services;
using OrderService.api.Specifications;

namespace OrderService.api.Workers;

/// <summary>
/// Baggrundstjeneste, der med jævne mellemrum finder checkouts, hvis deadline er overskredet, og beder
/// <see cref="CheckoutProcessManager"/> om at annullere dem. Uden den ville en checkout hænge for evigt, hvis
/// en service aldrig svarer.
/// </summary>
public class CheckoutDeadlineWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    private readonly ILogger<CheckoutDeadlineWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public CheckoutDeadlineWorker(ILogger<CheckoutDeadlineWorker> logger, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await CancelExpiredCheckoutsAsync(stoppingToken);
                await ReconcileStockReleasesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Checking checkout deadlines and stock recovery failed");
            }
        }
    }

    private async Task ReconcileStockReleasesAsync(CancellationToken stoppingToken)
    {
        var now = DateTimeOffset.UtcNow;
        List<CheckoutProcess> due;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<CheckoutProcess>>();
            due = await repository.ListAsync(new StockReleaseRecoveryDueSpec(now), stoppingToken);
        }

        foreach (var checkout in due)
        {
            stoppingToken.ThrowIfCancellationRequested();
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var manager = scope.ServiceProvider.GetRequiredService<CheckoutProcessManager>();
                await manager.ReconcileStockReleaseAsync(checkout.CheckoutId, now);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Checking stock release for checkout {CheckoutId} failed", checkout.CheckoutId);
            }
        }
    }

    private async Task CancelExpiredCheckoutsAsync(CancellationToken stoppingToken)
    {
        List<Guid> expired;

        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<CheckoutProcess>>();
            var checkouts = await repository.ListAsync(new ExpiredCheckoutsSpec(DateTimeOffset.Now), stoppingToken);
            expired = checkouts.Select(c => c.CheckoutId).ToList();
        }

        foreach (var checkoutId in expired)
        {
            try
            {
                // Hver checkout i sin egen scope, så en fejl ikke påvirker de andre
                await using var scope = _scopeFactory.CreateAsyncScope();
                var manager = scope.ServiceProvider.GetRequiredService<CheckoutProcessManager>();
                await manager.CancelAsync(checkoutId);

                _logger.LogWarning("Checkout {CheckoutId} timed out and is being cancelled", checkoutId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Cancelling checkout {CheckoutId} failed", checkoutId);
            }
        }
    }
}

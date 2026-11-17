using Microsoft.EntityFrameworkCore;

namespace BasketService.api.Data;

public class BasketContextSeed
{
    public static async Task SeedAsync(BasketContext basketContext,
        ILogger logger,
        int retry = 0)
    {
        var retryForAvailability = retry;
        try
        {
            if (basketContext.Database.IsSqlServer())
            {
                await basketContext.Database.MigrateAsync();
            }
        }
        catch (Exception ex)
        {
            if (retryForAvailability >= 10) throw;

            retryForAvailability++;

            logger.LogError(ex, "Basket database migration failed. Retrying attempt {RetryAttempt}", retryForAvailability);
            await SeedAsync(basketContext, logger, retryForAvailability);
        }
    }
}

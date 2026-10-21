using Microsoft.EntityFrameworkCore;
using OrderService.api.Entities.OrderAggregate;

namespace OrderService.api.Data;

public class OrderContextSeed
{
    public static async Task SeedAsync(OrderDbContext orderContext,
        ILogger logger,
        int retry = 0)
    {
        var retryForAvailability = retry;
        try
        {
            if (orderContext.Database.IsSqlServer())
            {
                orderContext.Database.Migrate();
            }

            if (!await orderContext.Orders.AnyAsync())
            {
                await orderContext.Orders.AddRangeAsync(GetPreconfiguredOrders());

                await orderContext.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            if (retryForAvailability >= 10) throw;

            retryForAvailability++;

            logger.LogError(ex.Message);
            await SeedAsync(orderContext, logger, retryForAvailability);
        }
    }

    static IEnumerable<Order> GetPreconfiguredOrders()
    {
        return new List<Order>
        {
            new Order(
                "demouser@microsoft.com",
                new Address("123 Main St.", "Kent", "OH", "United States", "44240"),
                new List<OrderItem>
                {
                    new OrderItem(new CatalogItemOrdered(1, ".NET Bot Black Sweatshirt", "http://localhost/images/products/1.png"), 19.50m, 2),
                    new OrderItem(new CatalogItemOrdered(2, ".NET Black & White Mug", "http://localhost/images/products/2.png"), 8.50m, 1)
                })
        };
    }
}

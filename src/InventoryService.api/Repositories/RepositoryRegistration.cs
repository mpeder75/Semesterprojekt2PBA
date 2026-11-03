using InventoryService.api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace InventoryService.api.Repositories;

public static class RepositoryRegistration
{
    public static IServiceCollection AddInventoryRepositories(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<InventoryDbContext>(options => options.UseSqlServer(
            configuration.GetConnectionString("InventoryConnection")
                ?? throw new InvalidOperationException("Configure ConnectionStrings:InventoryConnection.")));
        services.AddScoped<IInventoryRepository, InventoryRepository>();
        return services;
    }

    public static async Task MigrateInventoryDatabaseAsync(
        this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await database.Database.MigrateAsync(cancellationToken);
    }
}

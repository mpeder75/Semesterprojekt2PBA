#nullable enable

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using global::InventoryService.api.Interfaces;
using global::InventoryService.api.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.InventoryService;

public class InventoryConcurrencyTests(InventoryApiFactory factory) : IClassFixture<InventoryApiFactory>
{
    private static int _nextId = 20000;

    private async Task<int> CreateStock()
    {
        var id = Interlocked.Increment(ref _nextId);
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/inventory", new { catalogItemId = id, quantity = 10 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return id;
    }

    [Fact]
    public async Task Stock_RowVersionIsGeneratedAndChangesAfterAnUpdate()
    {
        var id = await CreateStock();
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IInventoryRepository>();
        var initial = await repository.GetStockAsync(id, default);
        Assert.NotNull(initial);
        Assert.Equal(8, initial!.RowVersion.Length);
        var originalVersion = initial.RowVersion.ToArray();

        await using (var transaction = await repository.BeginTransactionAsync(default))
        {
            var item = await repository.GetStockForUpdateAsync(id, default);
            item!.AdjustQuantity(2);
            await transaction.CommitAsync(default);
            Assert.False(originalVersion.SequenceEqual(item.RowVersion));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaleStockUpdateOrDelete_IsRejectedWithoutLosingCommittedStock(bool delete)
    {
        var id = await CreateStock();
        using var firstScope = factory.Services.CreateScope();
        using var secondScope = factory.Services.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<IInventoryRepository>();
        var second = secondScope.ServiceProvider.GetRequiredService<IInventoryRepository>();
        var a = await first.GetStockForUpdateAsync(id, default);
        var stale = await second.GetStockForUpdateAsync(id, default);

        await using (var transaction = await first.BeginTransactionAsync(default))
        {
            a!.SetQuantity(12);
            await transaction.CommitAsync(default);
        }

        await using (var transaction = await second.BeginTransactionAsync(default))
        {
            if (delete) second.RemoveStock(stale!);
            else stale!.AdjustQuantity(1);
            await Assert.ThrowsAsync<InventoryConcurrencyException>(() => transaction.CommitAsync(default));
        }

        var stored = await second.GetStockAsync(id, default);
        Assert.NotNull(stored);
        Assert.Equal(12, stored!.Quantity);
    }

    [Fact]
    public async Task Reservation_RowVersionProtectsAgainstStaleReleaseWrites()
    {
        var id = await CreateStock();
        var order = Interlocked.Increment(ref _nextId);
        using var client = factory.CreateClient();
        using var reserved = await client.PostAsJsonAsync("/api/inventory/reservations", new
        {
            orderId = order, items = new[] { new { catalogItemId = id, quantity = 3 } }
        });
        Assert.Equal(HttpStatusCode.Created, reserved.StatusCode);

        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IInventoryRepository>();
        var stale = await repository.GetReservationForUpdateAsync(order, default);
        Assert.NotNull(stale);
        Assert.Equal(8, stale!.RowVersion.Length);
        var originalVersion = stale.RowVersion.ToArray();
        using var released = await client.PostAsync($"/api/inventory/reservations/{order}/release", null);
        Assert.Equal(HttpStatusCode.OK, released.StatusCode);

        await using (var transaction = await repository.BeginTransactionAsync(default))
        {
            stale.MarkReleased();
            await Assert.ThrowsAsync<InventoryConcurrencyException>(() => transaction.CommitAsync(default));
        }

        var current = await repository.GetReservationAsync(order, default);
        Assert.True(current!.IsReleased);
        Assert.False(originalVersion.SequenceEqual(current.RowVersion));
        Assert.Equal(0, (await repository.GetStockAsync(id, default))!.ReservedQuantity);
    }

    [Fact]
    public async Task RowVersionMigration_PreservesExistingStockAndReservations()
    {
        var isolated = new InventoryApiFactory();
        await isolated.InitializeAsync();
        try
        {
            var options = new DbContextOptionsBuilder<InventoryDbContext>()
                .UseSqlServer(isolated.ConnectionString).Options;
            await using var database = new InventoryDbContext(options);
            var migrations = database.GetService<IMigrator>();
            await migrations.MigrateAsync("20261103201706_AddReservations");

            // Seed the old schema deliberately: these columns predate the new EF concurrency tokens.
            await using (var connection = new SqlConnection(isolated.ConnectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO InventoryItems (CatalogItemId, Quantity, ReservedQuantity) VALUES (1, 10, 3);
                    INSERT INTO Reservations (OrderId, IsReleased) VALUES (42, 0);
                    INSERT INTO ReservationItems (OrderId, CatalogItemId, Quantity) VALUES (42, 1, 3);
                    """;
                await command.ExecuteNonQueryAsync();
            }

            await migrations.MigrateAsync();

            var stock = await database.InventoryItems.SingleAsync();
            Assert.Equal(10, stock.Quantity);
            Assert.Equal(3, stock.ReservedQuantity);
            Assert.Equal(8, stock.RowVersion.Length);
            var reservation = await database.Reservations.Include(x => x.Items).SingleAsync();
            Assert.Equal(42, reservation.OrderId);
            Assert.False(reservation.IsReleased);
            Assert.Single(reservation.Items);
            Assert.Equal(3, reservation.Items.Single().Quantity);
            Assert.Equal(8, reservation.RowVersion.Length);
        }
        finally
        {
            await ((IAsyncLifetime)isolated).DisposeAsync();
        }
    }
}

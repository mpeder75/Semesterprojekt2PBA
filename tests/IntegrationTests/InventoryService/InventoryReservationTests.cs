#nullable enable

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using global::InventoryService.api.Interfaces;
using System.Collections.Generic;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.InventoryService;

public class InventoryReservationTests : IClassFixture<InventoryApiFactory>, IDisposable
{
    private static int _nextId = 1000;
    private readonly InventoryApiFactory _factory;
    private readonly HttpClient _client;
    private static int NextId() => Interlocked.Increment(ref _nextId);

    public InventoryReservationTests(InventoryApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<int> CreateStock(int quantity)
    {
        var id = NextId();
        var response = await _client.PostAsJsonAsync("/api/inventory", new { catalogItemId = id, quantity });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return id;
    }

    private Task<HttpResponseMessage> Reserve(int orderId, params (int Id, int Quantity)[] items) =>
        _client.PostAsJsonAsync("/api/inventory/reservations", new
        {
            orderId,
            items = items.Select(x => new { catalogItemId = x.Id, quantity = x.Quantity })
        });

    private async Task AssertStock(int id, int quantity, int reserved)
    {
        var stock = await _client.GetFromJsonAsync<Stock>($"/api/inventory/{id}");
        Assert.Equal(new Stock(id, quantity, reserved, quantity - reserved), stock);
    }

    [Fact]
    public async Task ReserveAndRelease_ArePersistentAndIdempotent()
    {
        var a = await CreateStock(10);
        var b = await CreateStock(5);
        var order = NextId();

        var reserved = await Reserve(order, (a, 3), (b, 2));
        Assert.Equal(HttpStatusCode.Created, reserved.StatusCode);
        Assert.Equal($"/api/inventory/reservations/{order}", reserved.Headers.Location?.ToString());
        var reservation = await reserved.Content.ReadFromJsonAsync<Reservation>();
        Assert.NotNull(reservation);
        Assert.Equal(order, reservation!.OrderId);
        Assert.Equal("Reserved", reservation.Status);
        Assert.Equal(2, reservation.Items.Length);
        await AssertStock(a, 10, 3);
        await AssertStock(b, 5, 2);

        Assert.Equal(HttpStatusCode.OK, (await Reserve(order, (b, 2), (a, 3))).StatusCode);
        await AssertStock(a, 10, 3);

        var release = await _client.PostAsync($"/api/inventory/reservations/{order}/release", null);
        Assert.Equal(HttpStatusCode.OK, release.StatusCode);
        Assert.Equal("Released", (await release.Content.ReadFromJsonAsync<Reservation>())!.Status);
        Assert.Equal(HttpStatusCode.OK,
            (await _client.PostAsync($"/api/inventory/reservations/{order}/release", null)).StatusCode);
        await AssertStock(a, 10, 0);
        await AssertStock(b, 5, 0);

        // A delayed retry must never reactivate a released order's allocation.
        var retry = await Reserve(order, (a, 3), (b, 2));
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal("Released", (await retry.Content.ReadFromJsonAsync<Reservation>())!.Status);
        await AssertStock(a, 10, 0);
    }

    [Fact]
    public async Task ReusingOrderWithDifferentItems_ReturnsConflictWithoutChangingStock()
    {
        var id = await CreateStock(10);
        var order = NextId();
        Assert.Equal(HttpStatusCode.Created, (await Reserve(order, (id, 3))).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await Reserve(order, (id, 4))).StatusCode);
        await AssertStock(id, 10, 3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedMultiItemReservation_ChangesNothing(bool unknownProduct)
    {
        var a = await CreateStock(10);
        var b = unknownProduct ? NextId() : await CreateStock(1);
        var order = NextId();

        var result = await Reserve(order, (a, 3), (b, 2));

        Assert.Equal(unknownProduct ? HttpStatusCode.NotFound : HttpStatusCode.Conflict, result.StatusCode);
        await AssertStock(a, 10, 0);
        if (!unknownProduct) await AssertStock(b, 1, 0);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.GetAsync($"/api/inventory/reservations/{order}")).StatusCode);
    }

    [Fact]
    public async Task ConcurrentOrders_CannotReserveTheSameLastUnit()
    {
        var id = await CreateStock(1);
        var outcomes = await Task.WhenAll(Reserve(NextId(), (id, 1)), Reserve(NextId(), (id, 1)));

        Assert.Single(outcomes, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Single(outcomes, x => x.StatusCode == HttpStatusCode.Conflict);
        await AssertStock(id, 1, 1);
    }

    [Fact]
    public async Task ConcurrentDuplicateReservationAndRelease_ApplyOnce()
    {
        var id = await CreateStock(10);
        var order = NextId();
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Reserve(order, (id, 3))));
        Assert.Single(outcomes, x => x.StatusCode == HttpStatusCode.Created);
        Assert.All(outcomes, x => Assert.Contains(x.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK }));
        await AssertStock(id, 10, 3);

        var releases = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            _client.PostAsync($"/api/inventory/reservations/{order}/release", null)));
        Assert.All(releases, x => Assert.Equal(HttpStatusCode.OK, x.StatusCode));
        await AssertStock(id, 10, 0);
    }

    [Fact]
    public async Task ReservedStock_CannotBeRemovedBySetAdjustmentOrDelete()
    {
        var id = await CreateStock(10);
        Assert.Equal(HttpStatusCode.Created, (await Reserve(NextId(), (id, 7))).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict,
            (await _client.PutAsJsonAsync($"/api/inventory/{id}", new { quantity = 6 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await _client.PostAsJsonAsync($"/api/inventory/{id}/adjustments", new { quantityChange = -4 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.DeleteAsync($"/api/inventory/{id}")).StatusCode);
        await AssertStock(id, 10, 7);
        Assert.Equal(HttpStatusCode.OK,
            (await _client.PutAsJsonAsync($"/api/inventory/{id}", new { quantity = 7 })).StatusCode);
        await AssertStock(id, 7, 7);
    }

    [Fact]
    public async Task CreateUpdateAdjustAndDelete_HaveDistinctBehavior()
    {
        var id = await CreateStock(0);
        Assert.Equal(HttpStatusCode.Conflict,
            (await _client.PostAsJsonAsync("/api/inventory", new { catalogItemId = id, quantity = 9 })).StatusCode);
        await AssertStock(id, 0, 0);
        Assert.Equal(HttpStatusCode.OK,
            (await _client.PostAsJsonAsync($"/api/inventory/{id}/adjustments", new { quantityChange = 3 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.DeleteAsync($"/api/inventory/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await _client.PostAsJsonAsync($"/api/inventory/{id}/adjustments", new { quantityChange = -3 })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/inventory/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/inventory/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/api/inventory/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.PutAsJsonAsync($"/api/inventory/{id}", new { quantity = 5 })).StatusCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"orderId\":0,\"items\":[{\"catalogItemId\":1,\"quantity\":1}]}")]
    [InlineData("{\"orderId\":1,\"items\":[]}")]
    [InlineData("{\"orderId\":1,\"items\":[null]}")]
    [InlineData("{\"orderId\":1,\"items\":[{\"catalogItemId\":1,\"quantity\":0}]}")]
    [InlineData("{\"orderId\":1,\"items\":[{\"catalogItemId\":0,\"quantity\":1}]}")]
    [InlineData("{\"orderId\":1,\"items\":[{\"catalogItemId\":1,\"quantity\":1},{\"catalogItemId\":1,\"quantity\":2}]}")]
    public async Task InvalidReservations_ReturnBadRequest(string json)
    {
        using var body = new StringContent(json, Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.PostAsync("/api/inventory/reservations", body)).StatusCode);
    }

    [Theory]
    [InlineData("confirm")]
    [InlineData("release")]
    public async Task MissingReservation_ReturnsNotFound(string operation)
    {
        var id = NextId();
        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.PostAsync($"/api/inventory/reservations/{id}/{operation}", null)).StatusCode);
    }

    [Fact]
    public async Task Confirmation_PersistsAcrossRestartAndKeepsStockAllocated()
    {
        var id = await CreateStock(10);
        var order = NextId();
        using var reserved = await Reserve(order, (id, 3));
        Assert.Equal(HttpStatusCode.Created, reserved.StatusCode);
        var reservationUrl = $"/api/inventory/reservations/{order}";

        using var confirmed = await _client.PostAsync($"{reservationUrl}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal("Confirmed", (await confirmed.Content.ReadFromJsonAsync<Reservation>())!.Status);
        await AssertStock(id, 10, 3);

        await using var restarted = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:InventoryConnection"] = _factory.ConnectionString })));
        using var client = restarted.CreateClient();
        var stored = await client.GetFromJsonAsync<Reservation>(reservationUrl);
        Assert.Equal("Confirmed", stored!.Status);

        using var retry = await client.PostAsync($"{reservationUrl}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal("Confirmed", (await retry.Content.ReadFromJsonAsync<Reservation>())!.Status);
        await AssertStock(id, 10, 3);

        using var release = await client.PostAsync($"{reservationUrl}/release", null);
        Assert.Equal(HttpStatusCode.OK, release.StatusCode);
        Assert.Equal("Released", (await release.Content.ReadFromJsonAsync<Reservation>())!.Status);
        using var rejected = await client.PostAsync($"{reservationUrl}/confirm", null);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        await AssertStock(id, 10, 0);
    }

    [Fact]
    public async Task ReservationAndDuplicateProtection_SurviveFreshApplicationHost()
    {
        var id = await CreateStock(10);
        var order = NextId();
        Assert.Equal(HttpStatusCode.Created, (await Reserve(order, (id, 3))).StatusCode);

        await using var restarted = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:InventoryConnection"] = _factory.ConnectionString })));
        using var client = restarted.CreateClient();
        var reservation = await client.GetFromJsonAsync<Reservation>($"/api/inventory/reservations/{order}");
        Assert.Equal("Reserved", reservation!.Status);
        var retry = await client.PostAsJsonAsync("/api/inventory/reservations", new
        {
            orderId = order, items = new[] { new { catalogItemId = id, quantity = 3 } }
        });
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        await AssertStock(id, 10, 3);
    }

    [Fact]
    public async Task DatabaseWriteFailure_RollsBackTheEntireReservationAndAllowsRetry()
    {
        var a = await CreateStock(10);
        var b = await CreateStock(10);
        var order = NextId();
        await using var connection = new SqlConnection(_factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        // Force a SQL write failure for this test-owned product, after request validation has passed.
        command.CommandText = $"ALTER TABLE InventoryItems ADD CONSTRAINT CK_Test_RejectReservation CHECK ([CatalogItemId] <> {b} OR [ReservedQuantity] = 0)";
        await command.ExecuteNonQueryAsync();
        try
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => Reserve(order, (a, 3), (b, 2)));
            await AssertStock(a, 10, 0);
            await AssertStock(b, 10, 0);
            Assert.Equal(HttpStatusCode.NotFound,
                (await _client.GetAsync($"/api/inventory/reservations/{order}")).StatusCode);
        }
        finally
        {
            command.CommandText = "ALTER TABLE InventoryItems DROP CONSTRAINT CK_Test_RejectReservation";
            await command.ExecuteNonQueryAsync();
        }
        Assert.Equal(HttpStatusCode.Created, (await Reserve(order, (a, 3), (b, 2))).StatusCode);
        await AssertStock(a, 10, 3);
        await AssertStock(b, 10, 2);
    }

    [Fact]
    public async Task ConcurrentStockAdjustments_CannotLoseUpdates()
    {
        var id = await CreateStock(0);
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            _client.PostAsJsonAsync($"/api/inventory/{id}/adjustments", new { quantityChange = 1 })));

        Assert.All(results, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        await AssertStock(id, 4, 0);
    }

    [Fact]
    public async Task AbandonedRepositoryTransaction_DoesNotLeakChangesIntoTheNextOperation()
    {
        var id = await CreateStock(10);
        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IInventoryRepository>();

        await using (var abandoned = await repository.BeginTransactionAsync(default))
        {
            var item = await repository.GetStockForUpdateAsync(id, default);
            item!.Reserve(3);
            // Leave without committing, as happens when a multi-product business operation fails.
        }

        await using (var next = await repository.BeginTransactionAsync(default))
        {
            var item = await repository.GetStockForUpdateAsync(id, default);
            Assert.Equal(0, item!.ReservedQuantity);
            item.Reserve(2);
            await next.CommitAsync(default);
        }

        await AssertStock(id, 10, 2);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"catalogItemId\":1}")]
    [InlineData("{\"quantity\":1}")]
    [InlineData("{\"catalogItemId\":1,\"quantity\":-1}")]
    public async Task InvalidStockCreation_ReturnsBadRequest(string json)
    {
        using var body = new StringContent(json, Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync("/api/inventory", body)).StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.BadRequest)]
    [InlineData(0, HttpStatusCode.BadRequest)]
    [InlineData(-11, HttpStatusCode.Conflict)]
    [InlineData(int.MaxValue, HttpStatusCode.BadRequest)]
    public async Task RejectedAdjustment_PreservesStock(int? change, HttpStatusCode expected)
    {
        var id = await CreateStock(10);
        var response = await _client.PostAsJsonAsync($"/api/inventory/{id}/adjustments", new { quantityChange = change });

        Assert.Equal(expected, response.StatusCode);
        await AssertStock(id, 10, 0);
    }

    private sealed record Stock(int CatalogItemId, int Quantity, int ReservedQuantity, int AvailableQuantity);
    private sealed record Reservation(int OrderId, string Status, Line[] Items);
    private sealed record Line(int CatalogItemId, int Quantity);
    public void Dispose() => _client.Dispose();
}

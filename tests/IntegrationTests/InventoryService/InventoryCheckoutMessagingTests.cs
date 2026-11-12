using System;
using System.Text.Json;
using System.Threading.Tasks;
using global::InventoryService.api.Interfaces;
using global::InventoryService.api.Messages;
using global::InventoryService.api.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.InventoryService;

public class InventoryCheckoutMessagingTests(InventoryApiFactory factory) : IClassFixture<InventoryApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ReserveAndConfirm_UseTheOrderReplyFormat()
    {
        await CreateStockAsync(1, 5);
        var checkoutId = Guid.NewGuid();
        var reserved = await SendAsync("ReserveStock", checkoutId,
            new { orderId = 101, items = new[] { new { catalogItemId = 1, quantity = 3 } } });
        Assert.Equal("StockReserved", reserved.MessageType);
        Assert.Equal(101, reserved.Payload.GetProperty("orderId").GetInt32());

        var confirmed = await SendAsync("ConfirmStockReservation", checkoutId, new { orderId = 101 });
        Assert.Equal("StockReservationConfirmed", confirmed.MessageType);
        Assert.Equal("Confirmed", confirmed.Payload.GetProperty("status").GetString());
        Assert.Equal(3, await ReadReservedQuantityAsync(1));
    }

    [Fact]
    public async Task StockRejection_ReportsTheShortProductAndRollsBackTheAllocation()
    {
        await CreateStockAsync(2, 5);
        await CreateStockAsync(3, 1);
        var checkoutId = Guid.NewGuid();
        var request = new
        {
            orderId = 102,
            items = new[] { new { catalogItemId = 2, quantity = 2 }, new { catalogItemId = 3, quantity = 3 } }
        };
        var rejected = await SendAsync("ReserveStock", checkoutId, request);

        Assert.Equal("StockReservationRejected", rejected.MessageType);
        Assert.False(string.IsNullOrWhiteSpace(rejected.Payload.GetProperty("reason").GetString()));
        var shortage = Assert.Single(rejected.Payload.GetProperty("shortages").EnumerateArray());
        Assert.Equal(3, shortage.GetProperty("catalogItemId").GetInt32());
        Assert.Equal(3, shortage.GetProperty("requestedQuantity").GetInt32());
        Assert.Equal(1, shortage.GetProperty("availableQuantity").GetInt32());
        Assert.Equal(0, await ReadReservedQuantityAsync(2));
        Assert.Equal(0, await ReadReservedQuantityAsync(3));

        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IStockService>().SetAsync(3, 10, default);

        var replay = await SendAsync("ReserveStock", checkoutId, request);
        Assert.Equal("StockReservationRejected", replay.MessageType);
        Assert.Equal(rejected.Payload.GetRawText(), replay.Payload.GetRawText());
        var status = await SendAsync("GetStockReservationStatus", checkoutId, new { orderId = 102 });
        Assert.Equal("Rejected", status.Payload.GetProperty("status").GetString());
        Assert.Equal(0, await ReadReservedQuantityAsync(3));
    }

    [Fact]
    public async Task EarlyRelease_BlocksLateReserveButAllowsNewOrders()
    {
        await CreateStockAsync(4, 5);
        var checkoutId = Guid.NewGuid();
        var release = new { orderId = 103, reason = "Checkout timed out" };
        Assert.Equal("StockReservationReleased", (await SendAsync("ReleaseStockReservation", checkoutId, release)).MessageType);
        Assert.Equal("StockReservationReleased", (await SendAsync("ReleaseStockReservation", checkoutId, release)).MessageType);

        var items = new[] { new { catalogItemId = 4, quantity = 3 } };
        var late = await SendAsync("ReserveStock", checkoutId, new { orderId = 103, items });
        Assert.Equal("StockReservationRejected", late.MessageType);
        Assert.False(late.Payload.TryGetProperty("shortages", out _));
        Assert.Equal(0, await ReadReservedQuantityAsync(4));

        Assert.Equal("StockReserved",
            (await SendAsync("ReserveStock", Guid.NewGuid(), new { orderId = 104, items })).MessageType);
        Assert.Equal(3, await ReadReservedQuantityAsync(4));
    }

    [Fact]
    public async Task ReleaseRequiresMatchingCheckoutAndCanBeVerifiedByStatus()
    {
        const int orderId = 105;
        var checkoutId = Guid.NewGuid();
        await CreateStockAsync(5, 10);

        var absent = await SendAsync("GetStockReservationStatus", checkoutId, new { orderId });
        Assert.Equal("Absent", absent.Payload.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, absent.Payload.GetProperty("checkoutId").ValueKind);

        await SendAsync("ReserveStock", checkoutId,
            new { orderId, items = new[] { new { catalogItemId = 5, quantity = 3 } } });
        var reserved = await SendAsync("GetStockReservationStatus", checkoutId, new { orderId });
        Assert.Equal("Reserved", reserved.Payload.GetProperty("status").GetString());
        await SendAsync("ConfirmStockReservation", checkoutId, new { orderId });
        var confirmed = await SendAsync("GetStockReservationStatus", checkoutId, new { orderId });
        Assert.Equal("Confirmed", confirmed.Payload.GetProperty("status").GetString());

        var wrongCheckout = await SendAsync("ReleaseStockReservation", Guid.NewGuid(), new { orderId });
        Assert.Equal("StockReleaseRejected", wrongCheckout.MessageType);
        Assert.Equal("CheckoutBindingMismatch", wrongCheckout.Payload.GetProperty("code").GetString());
        var wrongOrder = await SendAsync("ReleaseStockReservation", checkoutId, new { orderId = 106 });
        Assert.Equal("StockReleaseRejected", wrongOrder.MessageType);
        Assert.Equal("CheckoutBindingMismatch", wrongOrder.Payload.GetProperty("code").GetString());
        Assert.Equal(3, await ReadReservedQuantityAsync(5));

        await SendAsync("ReleaseStockReservation", checkoutId, new { orderId });
        var status = await SendAsync("GetStockReservationStatus", checkoutId, new { orderId });
        Assert.Equal("StockReservationStatusReturned", status.MessageType);
        Assert.Equal("Released", status.Payload.GetProperty("status").GetString());
        Assert.Equal(orderId, status.Payload.GetProperty("orderId").GetInt32());
        Assert.Equal(checkoutId, status.Payload.GetProperty("checkoutId").GetGuid());
        Assert.Equal(0, await ReadReservedQuantityAsync(5));
    }

    private async Task<CheckoutMessageEnvelope> SendAsync(string messageType, Guid checkoutId, object payload)
    {
        var messageId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var json = JsonSerializer.Serialize(new
        {
            messageId, checkoutId, correlationId, messageType, schemaVersion = 1,
            sentAtUtc = DateTimeOffset.UtcNow, replyTo = "order.checkout.replies", payload
        }, JsonOptions);
        var request = JsonSerializer.Deserialize<CheckoutMessageEnvelope>(json, JsonOptions)!;
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<InventoryCheckoutHandler>();
        var reply = await handler.HandleAsync(request, default);
        Assert.Equal(checkoutId, reply.CheckoutId);
        Assert.Equal(correlationId, reply.CorrelationId);
        Assert.Equal(messageId, reply.CausationId);
        Assert.NotEqual(messageId, reply.MessageId);
        return reply;
    }

    private async Task CreateStockAsync(int id, int quantity)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IStockService>().CreateAsync(id, quantity, default);
    }

    private async Task<int> ReadReservedQuantityAsync(int id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var item = await scope.ServiceProvider.GetRequiredService<IStockService>().GetAsync(id, default);
        return item!.ReservedQuantity;
    }
}

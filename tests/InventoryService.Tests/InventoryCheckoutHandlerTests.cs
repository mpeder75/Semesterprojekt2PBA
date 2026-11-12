using System.Text.Json;
using InventoryService.api.Entities;
using InventoryService.api.Interfaces;
using InventoryService.api.Messages;
using InventoryService.api.Services;
using NSubstitute;

namespace InventoryService.Tests;

public class InventoryCheckoutHandlerTests
{
    private readonly IStockService _stock = Substitute.For<IStockService>();

    [Theory]
    [InlineData("""{"orderId":42,"items":[{"catalogItemId":1,"quantity":0}]}""")]
    [InlineData("""{"orderId":42,"items":[{"catalogItemId":1,"quantity":-1}]}""")]
    [InlineData("""{"orderId":42,"items":[{"catalogItemId":0,"quantity":1}]}""")]
    [InlineData("""{"orderId":0,"items":[{"catalogItemId":1,"quantity":1}]}""")]
    [InlineData("""{"orderId":42,"items":[{"catalogItemId":1,"quantity":1},{"catalogItemId":1,"quantity":2}]}""")]
    [InlineData("""{"orderId":42,"items":[]}""")]
    [InlineData("""{"orderId":42,"items":[null]}""")]
    [InlineData("""{"orderId":42}""")]
    public async Task InvalidReservation_ReturnsCorrelatedRejectionWithoutChangingStock(string payload)
    {
        var request = Request(payload);
        _stock.RejectForCheckoutAsync(42, request.CheckoutId, Arg.Any<string>(), default)
            .Returns(call => StockReservation.RecordRejection(42, request.CheckoutId, "InvalidReservation", call.Arg<string>()));

        var reply = await new InventoryCheckoutHandler(_stock).HandleAsync(request, default);

        Assert.Equal("StockReservationRejected", reply.MessageType);
        Assert.Equal(request.CheckoutId, reply.CheckoutId);
        Assert.Equal(request.CorrelationId, reply.CorrelationId);
        Assert.Equal(request.MessageId, reply.CausationId);
        Assert.False(string.IsNullOrWhiteSpace(reply.Payload.GetProperty("reason").GetString()));
        await _stock.DidNotReceiveWithAnyArgs().ReserveForCheckoutAsync(
            default, default, default(IReadOnlyCollection<ReservationItem>)!, default);
    }

    [Theory]
    [InlineData("""{"orderId":42,"items":"invalid"}""")]
    [InlineData("""{"orderId":42,"items":[{"catalogItemId":1,"quantity":"invalid"}]}""")]
    public async Task InvalidJsonShape_RemainsAMalformedMessage(string payload)
    {
        await Assert.ThrowsAsync<JsonException>(() =>
            new InventoryCheckoutHandler(_stock).HandleAsync(Request(payload), default));
    }

    [Fact]
    public async Task MissingCorrelation_RemainsAMalformedMessage()
    {
        var request = Request("""{"orderId":42,"items":[{"catalogItemId":1,"quantity":1}]}""")
            with { CorrelationId = Guid.Empty };

        await Assert.ThrowsAsync<JsonException>(() =>
            new InventoryCheckoutHandler(_stock).HandleAsync(request, default));
    }

    private static CheckoutMessageEnvelope Request(string payload) => new()
    {
        MessageId = Guid.NewGuid(),
        CheckoutId = Guid.NewGuid(),
        CorrelationId = Guid.NewGuid(),
        MessageType = "ReserveStock",
        ReplyTo = "order.checkout.replies",
        Payload = JsonSerializer.Deserialize<JsonElement>(payload)
    };
}

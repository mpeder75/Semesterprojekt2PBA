using System.Text.Json;
using System.Text.Json.Serialization;
using InventoryService.api.Entities;
using InventoryService.api.Interfaces;
using InventoryService.api.Messages;

namespace InventoryService.api.Services;

public sealed class InventoryCheckoutHandler(IStockService stock)
{
    public async Task<CheckoutMessageEnvelope> HandleAsync(
        CheckoutMessageEnvelope request, CancellationToken cancellationToken)
    {
        if (request.SchemaVersion != 1 || request.MessageId == Guid.Empty || request.CheckoutId == Guid.Empty
            || request.CorrelationId == Guid.Empty || request.ReplyTo != CheckoutQueues.Replies
            || request.Payload.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Invalid checkout request envelope.");
        }

        var (messageType, payload) = request.MessageType switch
        {
            "ReserveStock" => await ReserveAsync(ReadPayload<ReserveStockPayload>(request), cancellationToken),
            "ConfirmStockReservation" => await ConfirmAsync(ReadPayload<OrderPayload>(request), cancellationToken),
            "ReleaseStockReservation" => await ReleaseAsync(ReadPayload<OrderPayload>(request), cancellationToken),
            _ => throw new JsonException($"Unknown inventory request type: {request.MessageType}.")
        };

        return new CheckoutMessageEnvelope
        {
            MessageId = Guid.NewGuid(),
            CheckoutId = request.CheckoutId,
            CorrelationId = request.CorrelationId,
            CausationId = request.MessageId,
            MessageType = messageType,
            SentAtUtc = DateTimeOffset.UtcNow,
            Payload = JsonSerializer.SerializeToElement(payload, CheckoutMessageEnvelope.JsonOptions)
        };
    }

    private async Task<(string, object)> ReserveAsync(ReserveStockPayload payload, CancellationToken cancellationToken)
    {
        if (payload.Items is not { Length: > 0 } || payload.Items.Any(item => item is null))
            throw new JsonException("A stock reservation requires product lines.");

        var items = payload.Items.Select(item => new ReservationItem(item!.CatalogItemId, item.Quantity)).ToArray();
        try
        {
            var (reservation, _) = await stock.ReserveAsync(payload.OrderId, items, cancellationToken);
            if (reservation.IsReleased)
                return ("StockReservationRejected", new StockRejectedPayload("This order's reservation was released."));

            return ("StockReserved", new StockReservedPayload(payload.OrderId));
        }
        catch (InventoryConflictException error) when (!error.Retryable)
        {
            if (error.Shortage is { } shortage)
                return ("StockReservationRejected", new StockRejectedPayload(error.Message, [shortage]));

            return ("StockReservationRejected", new StockRejectedPayload(error.Message));
        }
        catch (KeyNotFoundException error)
        {
            return ("StockReservationRejected", new StockRejectedPayload(error.Message));
        }
    }

    private async Task<(string, object)> ConfirmAsync(OrderPayload payload, CancellationToken cancellationToken)
    {
        try
        {
            await stock.ConfirmAsync(payload.OrderId, cancellationToken);
            return ("StockReservationConfirmed", new EmptyPayload());
        }
        catch (InventoryConflictException error) when (!error.Retryable)
        {
            return ("StockConfirmationRejected", new StockRejectedPayload(error.Message));
        }
        catch (KeyNotFoundException error)
        {
            return ("StockConfirmationRejected", new StockRejectedPayload(error.Message));
        }
    }

    private async Task<(string, object)> ReleaseAsync(OrderPayload payload, CancellationToken cancellationToken)
    {
        await stock.ReleaseForCheckoutAsync(payload.OrderId, cancellationToken);
        return ("StockReservationReleased", new EmptyPayload());
    }

    private static T ReadPayload<T>(CheckoutMessageEnvelope request) where T : class =>
        request.Payload.Deserialize<T>(CheckoutMessageEnvelope.JsonOptions)
        ?? throw new JsonException("Missing inventory request payload.");

    private sealed record OrderPayload(int OrderId);
    private sealed record ReserveStockPayload(int OrderId, StockLine?[]? Items);
    private sealed record StockLine(int CatalogItemId, int Quantity);
    private sealed record StockReservedPayload(int OrderId);
    private sealed record StockRejectedPayload(string Reason,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] StockShortage[]? Shortages = null);
    private sealed record EmptyPayload;
}

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
            "ReserveStock" => await ReserveAsync(ReadPayload<ReserveStockPayload>(request), request.CheckoutId, cancellationToken),
            "ConfirmStockReservation" => await ConfirmAsync(ReadPayload<OrderPayload>(request), request.CheckoutId, cancellationToken),
            "ReleaseStockReservation" => await ReleaseAsync(ReadPayload<OrderPayload>(request), request.CheckoutId, cancellationToken),
            "GetStockReservationStatus" => await GetStatusAsync(ReadPayload<OrderPayload>(request), cancellationToken),
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

    private async Task<(string, object)> ReserveAsync(
        ReserveStockPayload payload, Guid checkoutId, CancellationToken cancellationToken)
    {
        const string invalidLines = "A positive order ID and at least one product with a positive quantity are required.";
        if (payload.OrderId <= 0)
            return ("StockReservationRejected", new StockRejectedPayload(payload.OrderId, "InvalidReservation", invalidLines));

        string? rejection = null;
        if (payload.Items is not { Length: > 0 }
            || payload.Items.Any(item => item is null || item.CatalogItemId <= 0 || item.Quantity <= 0))
        {
            rejection = invalidLines;
        }
        else if (payload.Items.Select(item => item!.CatalogItemId).Distinct().Count() != payload.Items.Length)
        {
            rejection = "Each product must be listed once in a stock reservation.";
        }

        try
        {
            var reservation = rejection is null
                ? await stock.ReserveForCheckoutAsync(payload.OrderId, checkoutId,
                    payload.Items!.Select(item => new ReservationItem(item!.CatalogItemId, item.Quantity)).ToArray(),
                    cancellationToken)
                : await stock.RejectForCheckoutAsync(payload.OrderId, checkoutId, rejection, cancellationToken);

            if (reservation.Status == ReservationStatus.Rejected)
            {
                var shortages = reservation.RejectionProductId is int productId
                    && reservation.RejectionRequestedQuantity is int requested
                    && reservation.RejectionAvailableQuantity is int available
                    ? new[] { new StockShortage(productId, requested, available) }
                    : null;
                return ("StockReservationRejected", new StockRejectedPayload(payload.OrderId,
                    reservation.RejectionCode!, reservation.RejectionReason!, reservation.Status.ToString(), shortages));
            }
            if (reservation.IsReleased)
                return ("StockReservationRejected", new StockRejectedPayload(payload.OrderId, "ReservationReleased",
                    "This order's reservation was released.", reservation.Status.ToString()));

            return ("StockReserved", new ReservationStatusPayload(reservation.OrderId, reservation.CheckoutId,
                reservation.Status.ToString()));
        }
        catch (InventoryConflictException error) when (!error.Retryable)
        {
            return ("StockReservationRejected", new StockRejectedPayload(payload.OrderId,
                error.Code ?? "ReservationConflict", error.Message));
        }
    }

    private async Task<(string, object)> ConfirmAsync(OrderPayload payload, Guid checkoutId, CancellationToken cancellationToken)
    {
        if (payload.OrderId <= 0)
            return ("StockConfirmationRejected", InvalidOrder(payload));

        try
        {
            var reservation = await stock.ConfirmAsync(payload.OrderId, cancellationToken, checkoutId);
            return ("StockReservationConfirmed", new ReservationStatusPayload(reservation.OrderId, reservation.CheckoutId,
                reservation.Status.ToString()));
        }
        catch (InventoryConflictException error) when (!error.Retryable)
        {
            return ("StockConfirmationRejected", new StockRejectedPayload(payload.OrderId,
                error.Code ?? "ConfirmationConflict", error.Message));
        }
        catch (KeyNotFoundException error)
        {
            return ("StockConfirmationRejected", new StockRejectedPayload(payload.OrderId, "ReservationAbsent", error.Message));
        }
    }

    private async Task<(string, object)> ReleaseAsync(OrderPayload payload, Guid checkoutId, CancellationToken cancellationToken)
    {
        if (payload.OrderId <= 0)
            return ("StockReleaseRejected", InvalidOrder(payload));

        try
        {
            var reservation = await stock.ReleaseForCheckoutAsync(payload.OrderId, checkoutId, cancellationToken);
            return ("StockReservationReleased", new ReservationStatusPayload(reservation.OrderId, reservation.CheckoutId,
                reservation.Status.ToString()));
        }
        catch (InventoryConflictException error) when (!error.Retryable)
        {
            return ("StockReleaseRejected", new StockRejectedPayload(payload.OrderId,
                error.Code ?? "ReleaseConflict", error.Message));
        }
        catch (KeyNotFoundException error)
        {
            return ("StockReleaseRejected", new StockRejectedPayload(payload.OrderId, "ReservationStockMissing", error.Message));
        }
    }

    private async Task<(string, object)> GetStatusAsync(OrderPayload payload, CancellationToken cancellationToken)
    {
        if (payload.OrderId <= 0)
            throw new JsonException("A positive order ID is required for a reservation status query.");

        var reservation = await stock.GetReservationAsync(payload.OrderId, cancellationToken);
        return ("StockReservationStatusReturned", new ReservationStatusPayload(payload.OrderId, reservation?.CheckoutId,
            reservation?.Status.ToString() ?? "Absent"));
    }

    private static StockRejectedPayload InvalidOrder(OrderPayload payload) =>
        new(payload.OrderId, "InvalidOrderId", "A positive order ID is required.");

    private static T ReadPayload<T>(CheckoutMessageEnvelope request) where T : class =>
        request.Payload.Deserialize<T>(CheckoutMessageEnvelope.JsonOptions)
        ?? throw new JsonException("Missing inventory request payload.");

    private sealed record OrderPayload(int OrderId);
    private sealed record ReserveStockPayload(int OrderId, StockLine?[]? Items);
    private sealed record StockLine(int CatalogItemId, int Quantity);
    private sealed record ReservationStatusPayload(int OrderId, Guid? CheckoutId, string Status);
    private sealed record StockRejectedPayload(int OrderId, string Code, string Reason,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Status = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] StockShortage[]? Shortages = null);
}

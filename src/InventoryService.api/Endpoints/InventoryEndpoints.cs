using InventoryService.api.Entities;
using InventoryService.api.Interfaces;
using InventoryService.api.Models.Requests;
using InventoryService.api.Models.Responses;
using InventoryService.api.Services;

namespace InventoryService.api.Endpoints;

public static class InventoryEndpoints
{
    public static void MapInventoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/inventory").WithTags("Inventory");
        group.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (InventoryConflictException error) { return Results.Conflict(error.Message); }
            catch (KeyNotFoundException error) { return Results.NotFound(error.Message); }
            catch (ArgumentException error) { return Results.BadRequest(error.Message); }
            catch (OverflowException) { return Results.BadRequest("The stock quantity exceeds the supported integer range."); }
        });

        var routes = new[]
        {
            group.MapGet("/{catalogItemId:int}", GetAsync).Produces<StockResponse>(),
            group.MapPost("", CreateAsync).Produces<StockResponse>(StatusCodes.Status201Created),
            group.MapPut("/{catalogItemId:int}", SetAsync).Produces<StockResponse>(),
            group.MapPost("/{catalogItemId:int}/adjustments", AdjustAsync).Produces<StockResponse>(),
            group.MapDelete("/{catalogItemId:int}", DeleteAsync).Produces(StatusCodes.Status204NoContent),
            group.MapPost("/reservations", ReserveAsync)
                .Produces<ReservationResponse>(StatusCodes.Status201Created).Produces<ReservationResponse>(),
            group.MapGet("/reservations/{orderId:int}", GetReservationAsync).Produces<ReservationResponse>(),
            group.MapPost("/reservations/{orderId:int}/confirm", ConfirmAsync).Produces<ReservationResponse>(),
            group.MapPost("/reservations/{orderId:int}/release", ReleaseAsync).Produces<ReservationResponse>()
        };
        foreach (var route in routes)
            route.Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound)
                .Produces(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> GetAsync(
        int catalogItemId, IStockService stock, CancellationToken cancellationToken)
    {
        if (catalogItemId <= 0)
        {
            return Results.BadRequest("The catalog item ID must be positive.");
        }

        var item = await stock.GetAsync(catalogItemId, cancellationToken);
        return item is null
            ? Results.NotFound()
            : Results.Ok(ToResponse(item));
    }

    private static async Task<IResult> CreateAsync(
        CreateStockRequest request, IStockService stock, CancellationToken cancellationToken)
    {
        if (request.CatalogItemId is null or <= 0 || request.Quantity is null or < 0)
            return Results.BadRequest("A positive catalog item ID and a non-negative integer quantity are required.");

        var item = await stock.CreateAsync(request.CatalogItemId.Value, request.Quantity.Value, cancellationToken);
        return Results.Created($"/api/inventory/{item.CatalogItemId}", ToResponse(item));
    }

    private static async Task<IResult> SetAsync(
        int catalogItemId, SetStockRequest request, IStockService stock, CancellationToken cancellationToken)
    {
        if (catalogItemId <= 0 || request.Quantity is null or < 0)
        {
            return Results.BadRequest("A positive catalog item ID and a non-negative integer quantity are required.");
        }

        var item = await stock.SetAsync(catalogItemId, request.Quantity.Value, cancellationToken);
        return Results.Ok(ToResponse(item));
    }

    private static async Task<IResult> AdjustAsync(
        int catalogItemId, AdjustStockRequest request, IStockService stock, CancellationToken cancellationToken)
    {
        if (catalogItemId <= 0 || request.QuantityChange is null or 0)
            return Results.BadRequest("A positive catalog item ID and a non-zero integer change are required.");
        var item = await stock.AdjustAsync(catalogItemId, request.QuantityChange.Value, cancellationToken);
        return Results.Ok(ToResponse(item));
    }

    private static async Task<IResult> DeleteAsync(
        int catalogItemId, IStockService stock, CancellationToken cancellationToken)
    {
        await stock.DeleteAsync(catalogItemId, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> ReserveAsync(
        ReserveStockRequest request, IStockService stock, CancellationToken cancellationToken)
    {
        if (request.OrderId is null or <= 0 || request.Items is not { Count: > 0 }
            || request.Items.Any(item => item is null || item.CatalogItemId is null or <= 0 || item.Quantity is null or <= 0))
            return Results.BadRequest("A positive order ID and at least one product with a positive quantity are required.");

        var items = request.Items.Select(x => new ReservationItem(
            x!.CatalogItemId.GetValueOrDefault(), x.Quantity.GetValueOrDefault())).ToList();
        var (reservation, created) = await stock.ReserveAsync(request.OrderId.Value, items, cancellationToken);
        return created
            ? Results.Created($"/api/inventory/reservations/{reservation.OrderId}", ToResponse(reservation))
            : Results.Ok(ToResponse(reservation));
    }

    private static async Task<IResult> GetReservationAsync(
        int orderId, IStockService stock, CancellationToken cancellationToken)
    {
        var reservation = await stock.GetReservationAsync(orderId, cancellationToken);
        return reservation is null ? Results.NotFound() : Results.Ok(ToResponse(reservation));
    }

    private static async Task<IResult> ConfirmAsync(
        int orderId, IStockService stock, CancellationToken cancellationToken) =>
        Results.Ok(ToResponse(await stock.ConfirmAsync(orderId, cancellationToken)));

    private static async Task<IResult> ReleaseAsync(
        int orderId, IStockService stock, CancellationToken cancellationToken) =>
        Results.Ok(ToResponse(await stock.ReleaseAsync(orderId, cancellationToken)));

    private static StockResponse ToResponse(InventoryItem item) =>
        new(item.CatalogItemId, item.Quantity, item.ReservedQuantity, item.AvailableQuantity);

    private static ReservationResponse ToResponse(StockReservation reservation)
    {
        var status = reservation switch
        {
            { IsReleased: true } => "Released",
            { IsConfirmed: true } => "Confirmed",
            _ => "Reserved"
        };
        return new(reservation.OrderId, status,
            reservation.Items.OrderBy(x => x.CatalogItemId)
                .Select(x => new ReservationItemResponse(x.CatalogItemId, x.Quantity)).ToList());
    }
}

using InventoryService.api.Entities;
using InventoryService.api.Interfaces;
using InventoryService.api.Services;
using NSubstitute;

namespace InventoryService.Tests;

public class StockServiceTests
{
    private readonly IInventoryRepository _repository = Substitute.For<IInventoryRepository>();
    private readonly IInventoryTransaction _transaction = Substitute.For<IInventoryTransaction>();
    private readonly StockService _service;

    public StockServiceTests()
    {
        _repository.BeginTransactionAsync(default).ReturnsForAnyArgs(_transaction);
        _service = new StockService(_repository);
    }

    [Fact]
    public async Task Reserve_AllocatesAllLinesAndSavesOneReservation()
    {
        var a = new InventoryItem(1, 10);
        var b = new InventoryItem(2, 5);
        _repository.GetStockForUpdateAsync(1, default).Returns(a);
        _repository.GetStockForUpdateAsync(2, default).Returns(b);

        var (reservation, created) = await _service.ReserveAsync(42,
            new[] { new ReservationItem(2, 2), new ReservationItem(1, 3) }, default);

        Assert.True(created);
        Assert.Equal(42, reservation.OrderId);
        Assert.Equal(3, a.ReservedQuantity);
        Assert.Equal(2, b.ReservedQuantity);
        _repository.Received(1).AddReservation(reservation);
        await _transaction.Received(1).CommitAsync(default);
    }

    [Fact]
    public async Task IdenticalReplayOfReleasedOrder_DoesNotAllocateAgain()
    {
        var existing = new StockReservation(42, new[] { new ReservationItem(1, 3) });
        existing.MarkReleased();
        _repository.GetReservationForUpdateAsync(42, default).Returns(existing);

        var (reservation, created) = await _service.ReserveAsync(42,
            new[] { new ReservationItem(1, 3) }, default);

        Assert.False(created);
        Assert.Same(existing, reservation);
        Assert.True(reservation.IsReleased);
        await _repository.DidNotReceiveWithAnyArgs().GetStockForUpdateAsync(default, default);
        _repository.DidNotReceiveWithAnyArgs().AddReservation(default!);
    }

    [Fact]
    public async Task ChangedOrderPayload_IsRejectedWithoutCommitting()
    {
        _repository.GetReservationForUpdateAsync(42, default)
            .Returns(new StockReservation(42, new[] { new ReservationItem(1, 3) }));

        await Assert.ThrowsAsync<InventoryConflictException>(() => _service.ReserveAsync(42,
            new[] { new ReservationItem(1, 4) }, default));

        await _transaction.DidNotReceiveWithAnyArgs().CommitAsync(default);
        await _transaction.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task InsufficientStock_DoesNotSaveAReservationOrCommit()
    {
        _repository.GetStockForUpdateAsync(1, default).Returns(new InventoryItem(1, 1));

        await Assert.ThrowsAsync<InventoryConflictException>(() => _service.ReserveAsync(42,
            new[] { new ReservationItem(1, 2) }, default));

        _repository.DidNotReceiveWithAnyArgs().AddReservation(default!);
        await _transaction.DidNotReceiveWithAnyArgs().CommitAsync(default);
        await _transaction.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task Release_ReturnsEveryLineToAvailabilityAndKeepsOnHand()
    {
        var a = new InventoryItem(1, 10);
        var b = new InventoryItem(2, 5);
        a.Reserve(3);
        b.Reserve(2);
        var reservation = new StockReservation(42,
            new[] { new ReservationItem(1, 3), new ReservationItem(2, 2) });
        _repository.GetReservationForUpdateAsync(42, default).Returns(reservation);
        _repository.GetStockForUpdateAsync(1, default).Returns(a);
        _repository.GetStockForUpdateAsync(2, default).Returns(b);

        var result = await _service.ReleaseAsync(42, default);

        Assert.True(result.IsReleased);
        Assert.Equal(10, a.AvailableQuantity);
        Assert.Equal(5, b.AvailableQuantity);
        await _transaction.Received(1).CommitAsync(default);
    }

    [Fact]
    public async Task RepeatedRelease_DoesNotLoadOrReleaseStockAgain()
    {
        var reservation = new StockReservation(42, new[] { new ReservationItem(1, 3) });
        reservation.MarkReleased();
        _repository.GetReservationForUpdateAsync(42, default).Returns(reservation);

        Assert.Same(reservation, await _service.ReleaseAsync(42, default));

        await _repository.DidNotReceiveWithAnyArgs().GetStockForUpdateAsync(default, default);
    }

    [Fact]
    public async Task SettingBelowReservedStock_IsRejectedWithoutCommitting()
    {
        var item = new InventoryItem(1, 10);
        item.Reserve(7);
        _repository.GetStockForUpdateAsync(1, default).Returns(item);

        await Assert.ThrowsAsync<InventoryConflictException>(() => _service.SetAsync(1, 6, default));

        Assert.Equal(10, item.Quantity);
        await _transaction.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task KnownConcurrencyFailure_ReloadsAndAppliesTheAdjustmentOnce()
    {
        var stale = new InventoryItem(1, 10);
        var current = new InventoryItem(1, 12);
        var retryTransaction = Substitute.For<IInventoryTransaction>();
        _repository.BeginTransactionAsync(default).Returns(_transaction, retryTransaction);
        _repository.GetStockForUpdateAsync(1, default).Returns(stale, current);
        _transaction.CommitAsync(default).Returns(Task.FromException(new InventoryConcurrencyException("Changed")));

        var result = await _service.AdjustAsync(1, 1, default);

        Assert.Same(current, result);
        Assert.Equal(13, result.Quantity);
        await _transaction.Received(1).DisposeAsync();
        await retryTransaction.Received(1).CommitAsync(default);
    }

    [Fact]
    public async Task UncertainCommitFailure_IsNotAutomaticallyRetried()
    {
        _repository.GetStockForUpdateAsync(1, default).Returns(new InventoryItem(1, 10));
        _transaction.CommitAsync(default).Returns(Task.FromException(new InvalidOperationException("Commit outcome unknown")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.AdjustAsync(1, 1, default));

        await _repository.Received(1).BeginTransactionAsync(default);
        await _transaction.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task RepeatedConcurrencyFailures_StopWithBusinessConflict()
    {
        _repository.GetStockForUpdateAsync(1, default).Returns(_ => new InventoryItem(1, 10));
        _transaction.CommitAsync(default).Returns(Task.FromException(new InventoryConcurrencyException("Changed")));

        await Assert.ThrowsAsync<InventoryConflictException>(() => _service.AdjustAsync(1, 1, default));

        await _repository.Received(5).BeginTransactionAsync(default);
    }

    [Fact]
    public async Task ConcurrentIdenticalOrder_RechecksReservationBeforeRejectingInsufficientStock()
    {
        var existing = new StockReservation(42, new[] { new ReservationItem(1, 7) });
        _repository.GetReservationForUpdateAsync(42, default).Returns((StockReservation?)null, existing);
        _repository.GetReservationAsync(42, default).Returns(existing);
        var currentStock = new InventoryItem(1, 10);
        currentStock.Reserve(7);
        _repository.GetStockForUpdateAsync(1, default).Returns(currentStock);

        var result = await _service.ReserveAsync(42, new[] { new ReservationItem(1, 7) }, default);

        Assert.False(result.Created);
        Assert.Same(existing, result.Reservation);
        Assert.Equal(7, currentStock.ReservedQuantity);
    }

    [Fact]
    public async Task ConcurrentRelease_RechecksReservationBeforeReleasingAlreadyReturnedUnits()
    {
        var initial = new StockReservation(42, new[] { new ReservationItem(1, 3) });
        var released = new StockReservation(42, new[] { new ReservationItem(1, 3) });
        released.MarkReleased();
        _repository.GetReservationForUpdateAsync(42, default).Returns(initial, released);
        _repository.GetReservationAsync(42, default).Returns(released);
        _repository.GetStockForUpdateAsync(1, default).Returns(new InventoryItem(1, 10));

        Assert.Same(released, await _service.ReleaseAsync(42, default));
    }
}

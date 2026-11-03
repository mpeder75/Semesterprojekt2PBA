using InventoryService.api.Entities;

namespace InventoryService.Tests;

public class InventoryItemTests
{
    [Theory]
    [InlineData(1, 0)]
    [InlineData(42, 10)]
    public void CreateStock_PreservesProductIdAndQuantity(int productId, int quantity)
    {
        var item = new InventoryItem(productId, quantity);

        Assert.Equal(productId, item.CatalogItemId);
        Assert.Equal(quantity, item.Quantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CreateStock_RejectsInvalidProductId(int productId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InventoryItem(productId, 10));
    }

    [Fact]
    public void CreateStock_RejectsNegativeQuantity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InventoryItem(1, -1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    [InlineData(int.MaxValue)]
    public void SetQuantity_ReplacesStockWithoutChangingProduct(int quantity)
    {
        var item = new InventoryItem(1, 10);

        item.SetQuantity(quantity);

        Assert.Equal(quantity, item.Quantity);
        Assert.Equal(1, item.CatalogItemId);
    }

    [Fact]
    public void SetQuantity_RejectsNegativeQuantityAndPreservesStock()
    {
        var item = new InventoryItem(1, 10);

        Assert.Throws<ArgumentOutOfRangeException>(() => item.SetQuantity(-1));

        Assert.Equal(10, item.Quantity);
    }

    [Theory]
    [InlineData(10, 5, 15)]
    [InlineData(10, -2, 8)]
    [InlineData(10, -10, 0)]
    [InlineData(0, 1, 1)]
    [InlineData(0, int.MaxValue, int.MaxValue)]
    public void AdjustQuantity_ChangesStock(int initial, int change, int expected)
    {
        var item = new InventoryItem(1, initial);

        item.AdjustQuantity(change);

        Assert.Equal(expected, item.Quantity);
    }

    [Fact]
    public void AdjustQuantity_RejectsZeroChangeAndPreservesStock()
    {
        var item = new InventoryItem(1, 10);

        Assert.Throws<ArgumentOutOfRangeException>(() => item.AdjustQuantity(0));

        Assert.Equal(10, item.Quantity);
    }

    [Theory]
    [InlineData(10, -11)]
    [InlineData(0, -1)]
    [InlineData(0, int.MinValue)]
    public void AdjustQuantity_RejectsInsufficientStockAndPreservesStock(int initial, int change)
    {
        var item = new InventoryItem(1, initial);

        Assert.Throws<InvalidOperationException>(() => item.AdjustQuantity(change));

        Assert.Equal(initial, item.Quantity);
    }

    [Fact]
    public void AdjustQuantity_RejectsOverflowAndPreservesStock()
    {
        var item = new InventoryItem(1, int.MaxValue);

        Assert.Throws<OverflowException>(() => item.AdjustQuantity(1));

        Assert.Equal(int.MaxValue, item.Quantity);
    }

    [Fact]
    public void Reserve_AllocatesAvailableStockWithoutReducingOnHand()
    {
        var item = new InventoryItem(1, 10);

        item.Reserve(3);

        Assert.Equal(10, item.Quantity);
        Assert.Equal(3, item.ReservedQuantity);
        Assert.Equal(7, item.AvailableQuantity);
    }

    [Fact]
    public void Reserve_AllAvailableStock_LeavesZeroAvailable()
    {
        var item = new InventoryItem(1, 3);
        item.Reserve(3);

        Assert.Equal(0, item.AvailableQuantity);
    }

    [Fact]
    public void Reserve_CannotAllocateAlreadyReservedUnits()
    {
        var item = new InventoryItem(1, 10);
        item.Reserve(7);

        Assert.Throws<InvalidOperationException>(() => item.Reserve(4));

        Assert.Equal(7, item.ReservedQuantity);
        Assert.Equal(3, item.AvailableQuantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Reserve_RejectsNonPositiveQuantity(int quantity)
    {
        var item = new InventoryItem(1, 10);

        Assert.Throws<ArgumentOutOfRangeException>(() => item.Reserve(quantity));
        Assert.Equal(0, item.ReservedQuantity);
    }

    [Fact]
    public void Release_ReturnsUnitsToAvailabilityWithoutChangingOnHand()
    {
        var item = new InventoryItem(1, 10);
        item.Reserve(7);

        item.Release(3);

        Assert.Equal(10, item.Quantity);
        Assert.Equal(4, item.ReservedQuantity);
        Assert.Equal(6, item.AvailableQuantity);
    }

    [Fact]
    public void Release_RejectsMoreThanReserved()
    {
        var item = new InventoryItem(1, 10);
        item.Reserve(3);

        Assert.Throws<InvalidOperationException>(() => item.Release(4));
        Assert.Equal(3, item.ReservedQuantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Release_RejectsNonPositiveQuantity(int quantity)
    {
        var item = new InventoryItem(1, 10);
        item.Reserve(3);

        Assert.Throws<ArgumentOutOfRangeException>(() => item.Release(quantity));
        Assert.Equal(3, item.ReservedQuantity);
    }

    [Fact]
    public void SetQuantity_CannotReduceStockBelowReservedQuantity()
    {
        var item = new InventoryItem(1, 10);
        item.Reserve(7);

        Assert.Throws<InvalidOperationException>(() => item.SetQuantity(6));
        Assert.Equal(10, item.Quantity);
        item.SetQuantity(7);
        Assert.Equal(0, item.AvailableQuantity);
    }

    [Fact]
    public void AdjustQuantity_CannotRemoveReservedStock()
    {
        var item = new InventoryItem(1, 10);
        item.Reserve(7);

        Assert.Throws<InvalidOperationException>(() => item.AdjustQuantity(-4));
        Assert.Equal(10, item.Quantity);
        item.AdjustQuantity(-3);
        Assert.Equal(7, item.Quantity);
        Assert.Equal(0, item.AvailableQuantity);
    }
}

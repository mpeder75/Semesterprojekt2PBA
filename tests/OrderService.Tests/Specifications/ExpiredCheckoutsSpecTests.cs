using OrderService.api.Entities.OrderAggregate;
using OrderService.api.Specifications;

namespace OrderService.Tests.Specifications;

public class ExpiredCheckoutsSpecTests
{
    private static readonly Address Address = new("Street", "City", "OH", "US", "11111");
    private static readonly TimeSpan Expired = TimeSpan.FromMinutes(-5);

    private static CheckoutProcess Checkout(TimeSpan timeout, CheckoutState state)
    {
        var checkout = new CheckoutProcess(Guid.NewGuid(), 1, "buyer", Address, timeout);
        checkout.MoveTo(state);
        return checkout;
    }

    [Theory]
    [InlineData(CheckoutState.AwaitingBasket)]
    [InlineData(CheckoutState.AwaitingCatalog)]
    [InlineData(CheckoutState.AwaitingStockReservation)]
    [InlineData(CheckoutState.AwaitingStockConfirmation)]
    public void ReturnsExpiredCheckoutsThatAreNotConfirmed(CheckoutState state)
    {
        var expired = Checkout(Expired, state);

        var result = new ExpiredCheckoutsSpec(DateTimeOffset.Now).Evaluate(new[] { expired }).ToList();

        Assert.Same(expired, Assert.Single(result));
    }

    [Theory]
    [InlineData(CheckoutState.OrderConfirmedCleanupPending)]
    [InlineData(CheckoutState.Completed)]
    [InlineData(CheckoutState.ReleasingStock)]
    [InlineData(CheckoutState.ReleasingBasket)]
    [InlineData(CheckoutState.Failed)]
    public void IgnoresExpiredCheckoutsThatAreConfirmedFinishedOrAlreadyCleaningUp(CheckoutState state)
    {
        var result = new ExpiredCheckoutsSpec(DateTimeOffset.Now).Evaluate(new[] { Checkout(Expired, state) }).ToList();

        Assert.Empty(result);
    }

    [Fact]
    public void IgnoresCheckoutsWhoseDeadlineHasNotPassed()
    {
        var result = new ExpiredCheckoutsSpec(DateTimeOffset.Now)
            .Evaluate(new[] { Checkout(TimeSpan.FromMinutes(2), CheckoutState.AwaitingBasket) })
            .ToList();

        Assert.Empty(result);
    }
}

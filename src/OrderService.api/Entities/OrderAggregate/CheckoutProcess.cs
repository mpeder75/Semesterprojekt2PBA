using OrderService.api.Interfaces;

namespace OrderService.api.Entities.OrderAggregate;

public class CheckoutProcess : BaseEntity, IAggregateRoot
{
    public Guid CheckoutId { get; private set; }
    public CheckoutState State { get; private set; }
    public int BasketId { get; private set; }   
    public string BuyerId { get; private set; }
    public Address ShipToAddress { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.Now;
    private readonly List<CheckoutLine> _lines = new();
    public IReadOnlyCollection<CheckoutLine> Lines => _lines.AsReadOnly();

    // Ef constructor
#pragma warning disable CS8618
    private CheckoutProcess() { }

    public CheckoutProcess(Guid checkoutId, int basketId, string buyerId, Address shipToAddress)
    {
        CheckoutId = checkoutId;
        State = CheckoutState.AwaitingBasket;
        BasketId = basketId;
        BuyerId = buyerId;
        ShipToAddress = shipToAddress;
    }

    public void SetLines(IEnumerable<CheckoutLine> lines)
    {
        _lines.Clear();
        _lines.AddRange(lines);
    }

    public void MoveTo(CheckoutState next)
    {
        State = next;
    }
}

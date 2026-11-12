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
    public string? FailureReason { get; private set; }
    private readonly List<CheckoutLine> _lines = new();
    public IReadOnlyCollection<CheckoutLine> Lines => _lines.AsReadOnly();

    public Guid? ExpectedCorrelationId { get; private set; }
    public DateTimeOffset? StockRecoveryDueAt { get; private set; }
    public CheckoutRecoveryStatus RecoveryStatus { get; private set; }
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    /// <summary>Hvor længe en checkout må vare, før den annulleres (før ordren er bekræftet).</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Tidspunktet, hvor checkouten annulleres, hvis den ikke er bekræftet. Tom for ældre checkouts, der blev
    /// oprettet før deadline fandtes (de annulleres ikke).
    /// </summary>
    public DateTimeOffset? DeadlineAt { get; private set; }

    // Ef constructor
#pragma warning disable CS8618
    private CheckoutProcess() { }

    public CheckoutProcess(Guid checkoutId, int basketId, string buyerId, Address shipToAddress, TimeSpan? timeout = null)
    {
        CheckoutId = checkoutId;
        State = CheckoutState.AwaitingBasket;
        BasketId = basketId;
        BuyerId = buyerId;
        ShipToAddress = shipToAddress;
        DeadlineAt = CreatedAt.Add(timeout ?? DefaultTimeout);
    }

    /// <summary>
    /// Er det samme request som det, checkouten blev startet med? Bruges til at afgøre, om en genbrugt
    /// Idempotency-Key er et ægte genforsøg (samme kurv, køber og adresse) eller en konflikt.
    /// </summary>
    public bool IsSameRequest(int basketId, string buyerId, Address shipToAddress)
    {
        return BasketId == basketId
               && BuyerId == buyerId
               && ShipToAddress.Street == shipToAddress.Street
               && ShipToAddress.City == shipToAddress.City
               && ShipToAddress.State == shipToAddress.State
               && ShipToAddress.Country == shipToAddress.Country
               && ShipToAddress.ZipCode == shipToAddress.ZipCode;
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

    public void ExpectReply(Guid correlationId)
    {
        ExpectedCorrelationId = correlationId;
    }

    public void ScheduleStockRecovery(DateTimeOffset dueAt)
    {
        StockRecoveryDueAt = dueAt;
    }

    public void MarkStockRecoveryRetrying()
    {
        if (RecoveryStatus == CheckoutRecoveryStatus.None)
            RecoveryStatus = CheckoutRecoveryStatus.Retrying;
    }

    public void MarkStockReleaseConflict(DateTimeOffset dueAt)
    {
        RecoveryStatus = CheckoutRecoveryStatus.NeedsAttention;
        StockRecoveryDueAt = dueAt;
        ExpectedCorrelationId = null;
    }

    public void FinishStockRelease()
    {
        State = CheckoutState.ReleasingBasket;
        RecoveryStatus = CheckoutRecoveryStatus.None;
        StockRecoveryDueAt = null;
        ExpectedCorrelationId = null;
    }

    public void SetFailure(string reason)
    {
        // Kolonnen er begrænset til 500 tegn
        FailureReason = reason.Length > 500 ? reason[..500] : reason;
    }
}

public enum CheckoutRecoveryStatus
{
    None,
    Retrying,
    NeedsAttention
}

using System;

namespace Microsoft.eShopWeb.ApplicationCore.Messages;

public class OrderSummaryMessage
{
    public int Id { get; set; }
    public AddressMessage Address { get; set; } = new();
    public decimal Total { get; set; }
    public DateTimeOffset OrderDate { get; set; }
}

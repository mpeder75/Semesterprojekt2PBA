using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Messages;

public class GetOrderResponse
{
    public bool Found { get; set; }
    public int Id { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public AddressMessage Address { get; set; } = new();
    public decimal Total { get; set; }
    public List<OrderItemMessage> Items { get; set; } = new();
}

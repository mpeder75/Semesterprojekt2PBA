using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Messages;

public class ListOrdersResponse
{
    public List<OrderSummaryMessage> Orders { get; set; } = new();
}

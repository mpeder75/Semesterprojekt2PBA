using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class UnavailableBasketItemsException : Exception
{
    public UnavailableBasketItemsException()
        : base("Some products are no longer available. Return to your basket and remove them before checking out.") { }
}

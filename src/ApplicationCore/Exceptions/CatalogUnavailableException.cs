using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class CatalogUnavailableException : Exception
{
    public CatalogUnavailableException(Exception inner) : base("Catalog is temporarily unavailable. Please try again later.", inner) { }
}

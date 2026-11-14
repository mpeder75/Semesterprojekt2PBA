namespace Microsoft.eShopWeb.Web.ViewModels;

/// <summary>En linje i kurven, som den sendes til BasketService.</summary>
public record BasketLine(int CatalogItemId, decimal UnitPrice, int Quantity);

/// <summary>BasketService kunne ikke nås eller afviste kurven. Meddelelsen kan vises til kunden uden detaljer.</summary>
public class BasketBridgeException : Exception
{
    public BasketBridgeException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

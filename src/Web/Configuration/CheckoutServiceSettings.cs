namespace Microsoft.eShopWeb.Web.Configuration;

/// <summary>
/// Indstillinger for checkout via OrderService (sektionen "CheckoutService" i konfigurationen).
/// </summary>
public class CheckoutServiceSettings
{
    public const string SectionName = "CheckoutService";

    /// <summary>
    /// Sand: Checkout starter hos OrderService, og kunden sendes til en statusside.
    /// Falsk (standard): monolittens egen checkout bruges som hidtil.
    /// </summary>
    public bool UseOrderService { get; set; }

    /// <summary>OrderService' adresse (gatewayen). Standard: http://localhost:5233/.</summary>
    public string? BaseUrl { get; set; }
}

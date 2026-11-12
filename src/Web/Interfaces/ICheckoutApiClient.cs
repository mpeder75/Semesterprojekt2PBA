using Microsoft.eShopWeb.Web.ViewModels;

namespace Microsoft.eShopWeb.Web.Interfaces;

/// <summary>
/// Klient til OrderService' checkout-API (gennem gatewayen). Web starter en checkout med StartAsync og følger
/// den med GetStatusAsync. Web sletter aldrig selv kurven; det gør BasketService, når checkouten er færdig.
/// </summary>
public interface ICheckoutApiClient
{
    /// <summary>
    /// Starter en checkout. Samme checkoutId med samme indhold er et genforsøg og giver den samme checkout tilbage.
    /// </summary>
    /// <param name="checkoutId">Klientens Idempotency-Key. Bruges som checkoutens id.</param>
    Task<StartCheckoutResult> StartAsync(
        Guid checkoutId, int basketId, string buyerId, CheckoutAddress shipToAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Henter status for en checkout. Returnerer null, hvis checkouten ikke findes.
    /// </summary>
    Task<CheckoutStatusViewModel?> GetStatusAsync(Guid checkoutId, CancellationToken cancellationToken = default);
}

using Microsoft.eShopWeb.Web.ViewModels;

namespace Microsoft.eShopWeb.Web.Interfaces;

/// <summary>
/// Klient til BasketService. Bruges som en bro ved checkout: monolitten ejer stadig kurven, men OrderService beder
/// BasketService om kurvens indhold, så kurven skal ligge dér, før checkouten startes.
/// </summary>
public interface IBasketApiClient
{
    /// <summary>
    /// Sørger for, at køberen har en kurv i BasketService med præcis disse linjer, og returnerer kurvens id dér.
    /// Kan gentages uden at lave dobbeltlinjer: en kurv fra et tidligere forsøg rettes til de rigtige antal.
    /// </summary>
    /// <exception cref="BasketBridgeException">BasketService kunne ikke nås eller svarede med en fejl.</exception>
    Task<int> ReplaceBasketAsync(string buyerId, IReadOnlyCollection<BasketLine> lines, CancellationToken cancellationToken = default);
}

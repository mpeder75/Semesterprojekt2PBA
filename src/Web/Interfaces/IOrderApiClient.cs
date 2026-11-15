using Microsoft.eShopWeb.Web.ViewModels;

namespace Microsoft.eShopWeb.Web.Interfaces;

/// <summary>
/// Klient til OrderService' ordre-endpoints. Bruges til "My orders" og ordredetaljer, når ordrerne ligger i OrderService
/// og ikke i monolittens database.
/// </summary>
public interface IOrderApiClient
{
    /// <summary>Alle ordrer, køberen har hos OrderService, med deres rigtige status.</summary>
    Task<IReadOnlyList<OrderViewModel>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// En ordre med varer. Returnerer null, hvis ordren ikke findes, eller hvis den ikke tilhører køberen
    /// (OrderService' detaljeendpoint kender ikke køberen, så ejerskabet tjekkes mod købers egen liste).
    /// </summary>
    Task<OrderDetailViewModel?> GetOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);
}

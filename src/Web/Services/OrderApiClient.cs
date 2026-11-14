using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Web.Interfaces;
using Microsoft.eShopWeb.Web.ViewModels;

namespace Microsoft.eShopWeb.Web.Services;

/// <summary>
/// Henter ordrer fra OrderService over HTTP. HttpClient'ens BaseAddress peger på OrderService (eller gatewayen).
/// </summary>
public class OrderApiClient : IOrderApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    public OrderApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<OrderViewModel>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buyerId);

        var response = await _httpClient.GetFromJsonAsync<OrderListResponse>(
            $"api/orders?buyerId={Uri.EscapeDataString(buyerId)}", JsonOptions, cancellationToken);

        return (response?.Orders ?? new List<OrderSummary>())
            .OrderByDescending(o => o.OrderDate)
            .Select(o => new OrderViewModel
            {
                OrderNumber = o.Id,
                OrderDate = o.OrderDate,
                Total = o.Total,
                Status = ToDisplayStatus(o.Status),
                ShippingAddress = ToAddress(o.Address)
            })
            .ToList();
    }

    public async Task<OrderDetailViewModel?> GetOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        // Detaljeendpointet kender ikke køberen, så ejerskabet tjekkes mod købers egen liste
        var own = await GetOrdersAsync(buyerId, cancellationToken);

        if (own.All(o => o.OrderNumber != orderId))
        {
            return null;
        }

        using var response = await _httpClient.GetAsync($"api/orders/{orderId}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<OrderDetailResponse>(JsonOptions, cancellationToken);
        var order = body?.Order;

        if (order is null)
        {
            return null;
        }

        return new OrderDetailViewModel
        {
            OrderNumber = order.Id,
            OrderDate = order.OrderDate,
            Total = order.Total,
            Status = ToDisplayStatus(order.Status),
            ShippingAddress = ToAddress(order.Address),
            OrderItems = order.OrderItems.Select(i => new OrderItemViewModel
            {
                ProductId = i.ProductId,
                ProductName = i.ProductName,
                PictureUrl = i.PictureUrl,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList()
        };
    }

    /// <summary>OrderService' status på en måde, en kunde forstår.</summary>
    private static string ToDisplayStatus(string status) => status switch
    {
        "Confirmed" => "Confirmed",
        "PendingInventory" => "Processing",
        "Rejected" => "Rejected",
        "Cancelled" => "Cancelled",
        _ => status
    };

    private static Address? ToAddress(AddressResponse? a) =>
        a is null ? null : new Address(a.Street, a.City, a.State, a.Country, a.ZipCode);

    private sealed class OrderListResponse
    {
        public List<OrderSummary> Orders { get; set; } = new();
    }

    private sealed class OrderSummary
    {
        public int Id { get; set; }
        public DateTimeOffset OrderDate { get; set; }
        public decimal Total { get; set; }
        public string Status { get; set; } = string.Empty;
        public AddressResponse? Address { get; set; }
    }

    private sealed class OrderDetailResponse
    {
        public OrderDetail? Order { get; set; }
    }

    private sealed class OrderDetail
    {
        public int Id { get; set; }
        public DateTimeOffset OrderDate { get; set; }
        public decimal Total { get; set; }
        public string Status { get; set; } = string.Empty;
        public AddressResponse? Address { get; set; }
        public List<OrderItemResponse> OrderItems { get; set; } = new();
    }

    private sealed class OrderItemResponse
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string PictureUrl { get; set; } = string.Empty;
        public decimal UnitPrice { get; set; }
        public int Units { get; set; }
    }

    private sealed class AddressResponse
    {
        public string Street { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string ZipCode { get; set; } = string.Empty;
    }
}

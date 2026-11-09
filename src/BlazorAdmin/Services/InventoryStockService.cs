#nullable enable

using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using BlazorShared;
using Microsoft.Extensions.Options;

namespace BlazorAdmin.Services;

public sealed class InventoryStockService(HttpClient httpClient, IOptions<BaseUrlConfiguration> configuration)
{
    private string StockUrl => configuration.Value.ApiBase + "inventory";

    public async Task<InventoryStock?> GetAsync(int catalogItemId)
    {
        using var response = await httpClient.GetAsync($"{StockUrl}/{catalogItemId}");
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<InventoryStock>()
            ?? throw new JsonException("Inventory returned an empty stock response.");
    }

    public async Task SaveAsync(int catalogItemId, int quantity, bool create)
    {
        using var response = create
            ? await httpClient.PostAsJsonAsync(StockUrl, new { catalogItemId, quantity })
            : await httpClient.PutAsJsonAsync($"{StockUrl}/{catalogItemId}", new { quantity });

        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(int catalogItemId)
    {
        using var response = await httpClient.DeleteAsync($"{StockUrl}/{catalogItemId}");
        response.EnsureSuccessStatusCode();
    }
}

public sealed record InventoryStock(int Quantity, int ReservedQuantity, int AvailableQuantity);

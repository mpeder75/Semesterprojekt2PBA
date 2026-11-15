using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.eShopWeb.Web.Interfaces;
using Microsoft.eShopWeb.Web.ViewModels;

namespace Microsoft.eShopWeb.Web.Services;

/// <summary>
/// Kalder BasketService' HTTP-endpoints. HttpClient'ens BaseAddress peger på BasketService (eller gatewayen) og sættes,
/// når klienten registreres.
/// </summary>
/// <remarks>
/// BasketService kan kun lægge varer til en kurv (antal lægges sammen) og rette antal ud fra linjens eget id.
/// Derfor lægger vi først alle linjer til og retter bagefter alle antal til det ønskede (nul fjerner en uønsket linje).
/// Så giver et nyt forsøg samme kurv og ikke dobbelte antal.
/// </remarks>
public class BasketApiClient : IBasketApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ILogger<BasketApiClient> _logger;

    public BasketApiClient(HttpClient httpClient, ILogger<BasketApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<int> ReplaceBasketAsync(string buyerId, IReadOnlyCollection<BasketLine> lines, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buyerId);
        ArgumentNullException.ThrowIfNull(lines);

        if (lines.Count == 0)
        {
            throw new ArgumentException("A basket needs at least one line.", nameof(lines));
        }

        try
        {
            BasketResponse? basket = null;

            foreach (var line in lines)
            {
                var add = new { catalogItemId = line.CatalogItemId, price = line.UnitPrice, quantity = line.Quantity };
                using var response = await _httpClient.PostAsJsonAsync(
                    $"basket/{Uri.EscapeDataString(buyerId)}/items", add, JsonOptions, cancellationToken);
                basket = await ReadBasketAsync(response, cancellationToken);
            }

            // De ønskede antal pr. vare. Alt andet i kurven (fra et tidligere forsøg) sættes til nul og fjernes.
            var wanted = lines
                .GroupBy(l => l.CatalogItemId)
                .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

            var quantities = basket!.Items.ToDictionary(
                item => item.Id.ToString(),
                item => wanted.GetValueOrDefault(item.CatalogItemId, 0));

            using var update = await _httpClient.PutAsJsonAsync(
                $"basket/{basket.Id}/items/quantities", new { quantities }, JsonOptions, cancellationToken);

            if (!update.IsSuccessStatusCode)
            {
                throw new BasketBridgeException($"BasketService answered {(int)update.StatusCode} when setting quantities.");
            }

            return basket.Id;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Could not reach BasketService to prepare the basket for checkout");
            throw new BasketBridgeException("BasketService could not be reached.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "BasketService did not answer in time");
            throw new BasketBridgeException("BasketService did not answer in time.", ex);
        }
    }

    private static async Task<BasketResponse> ReadBasketAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new BasketBridgeException($"BasketService answered {(int)response.StatusCode} when adding an item.");
        }

        var basket = await response.Content.ReadFromJsonAsync<BasketResponse>(JsonOptions, cancellationToken);

        if (basket is null || basket.Id <= 0)
        {
            throw new BasketBridgeException("BasketService returned no basket.");
        }

        return basket;
    }

    private sealed class BasketResponse
    {
        public int Id { get; set; }
        public List<BasketItemResponse> Items { get; set; } = new();
    }

    private sealed class BasketItemResponse
    {
        public int Id { get; set; }
        public int CatalogItemId { get; set; }
        public int Quantity { get; set; }
    }
}

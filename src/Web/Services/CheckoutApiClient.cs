using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.eShopWeb.Web.Interfaces;
using Microsoft.eShopWeb.Web.ViewModels;

namespace Microsoft.eShopWeb.Web.Services;

/// <summary>
/// Kalder OrderService' checkout-endpoints over HTTP. HttpClient'ens BaseAddress peger på gatewayen (eller direkte
/// på OrderService under udvikling) og sættes, når klienten registreres.
/// </summary>
public class CheckoutApiClient : ICheckoutApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ILogger<CheckoutApiClient> _logger;

    public CheckoutApiClient(HttpClient httpClient, ILogger<CheckoutApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<StartCheckoutResult> StartAsync(
        Guid checkoutId, int basketId, string buyerId, CheckoutAddress shipToAddress,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/orders/checkouts")
        {
            Content = JsonContent.Create(new { basketId, buyerId, shipToAddress }, options: JsonOptions)
        };
        request.Headers.Add("Idempotency-Key", checkoutId.ToString());

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Accepted)
            {
                return new StartCheckoutResult(StartCheckoutOutcome.Accepted, checkoutId);
            }

            var message = await response.Content.ReadAsStringAsync(cancellationToken);

            return response.StatusCode switch
            {
                HttpStatusCode.Conflict => new StartCheckoutResult(StartCheckoutOutcome.Conflict, Message: message),
                HttpStatusCode.BadRequest => new StartCheckoutResult(StartCheckoutOutcome.Rejected, Message: message),
                _ => Unavailable($"OrderService answered {(int)response.StatusCode}.")
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Could not reach OrderService to start checkout {CheckoutId}", checkoutId);
            return Unavailable("OrderService could not be reached.");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Starting checkout {CheckoutId} timed out", checkoutId);
            return Unavailable("OrderService did not answer in time.");
        }
    }

    public async Task<CheckoutStatusViewModel?> GetStatusAsync(Guid checkoutId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"api/orders/checkouts/{checkoutId}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CheckoutStatusViewModel>(JsonOptions, cancellationToken);
    }

    private static StartCheckoutResult Unavailable(string message) =>
        new(StartCheckoutOutcome.Unavailable, Message: message);
}

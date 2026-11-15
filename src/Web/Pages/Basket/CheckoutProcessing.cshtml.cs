using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Web.Interfaces;
using Microsoft.eShopWeb.Web.ViewModels;

namespace Microsoft.eShopWeb.Web.Pages.Basket;

/// <summary>
/// Viser fremdriften for en checkout, som OrderService styrer. Checkout-id'et står i adressen, så en genindlæsning
/// eller en tilbagevenden bruger den samme checkout. Siden henter selv status med mellemrum (handleren Status).
/// </summary>
[Authorize]
public class CheckoutProcessingModel : PageModel
{
    private readonly ICheckoutApiClient _checkoutApiClient;
    private readonly IBasketService _basketService;
    private readonly IBasketViewModelService _basketViewModelService;
    private readonly ILogger<CheckoutProcessingModel> _logger;

    public CheckoutProcessingModel(
        ICheckoutApiClient checkoutApiClient,
        IBasketService basketService,
        IBasketViewModelService basketViewModelService,
        ILogger<CheckoutProcessingModel> logger)
    {
        _checkoutApiClient = checkoutApiClient;
        _basketService = basketService;
        _basketViewModelService = basketViewModelService;
        _logger = logger;
    }

    public Guid CheckoutId { get; private set; }

    /// <summary>Status ved indlæsning. Tom, hvis OrderService ikke kunne nås lige nu.</summary>
    public CheckoutStatusViewModel? Checkout { get; private set; }

    public async Task<IActionResult> OnGet(Guid checkoutId)
    {
        CheckoutId = checkoutId;

        try
        {
            Checkout = await _checkoutApiClient.GetStatusAsync(checkoutId);
        }
        catch (HttpRequestException ex)
        {
            // Siden kan stadig vises; den hentede status prøver igen
            _logger.LogWarning(ex, "Could not read status for checkout {CheckoutId}", checkoutId);
            return Page();
        }

        if (Checkout?.Status == "Completed")
        {
            await ClearMonolithBasketAsync();
        }

        return Checkout is null ? NotFound() : Page();
    }

    /// <summary>
    /// Kaldes af sidens script. Svarer med status som JSON, eller 503 hvis OrderService ikke kan nås, så scriptet
    /// prøver igen. En fejl på statusopslaget er ikke bevis på, at checkouten fejlede.
    /// </summary>
    public async Task<IActionResult> OnGetStatus(Guid checkoutId)
    {
        try
        {
            var status = await _checkoutApiClient.GetStatusAsync(checkoutId);

            if (status?.Status == "Completed")
            {
                await ClearMonolithBasketAsync();
            }

            return status is null ? NotFound() : new JsonResult(status);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Could not read status for checkout {CheckoutId}", checkoutId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    /// <summary>
    /// Når købet er gennemført, ryddes kundens kurv i monolitten (BasketService har ryddet sin egen). Det er først nu, ikke
    /// ved start, så en mislykket checkout lader kunden beholde sin kurv. Kan gentages uden skade, og en fejl her
    /// må ikke ødelægge visningen af, at købet lykkedes.
    /// </summary>
    private async Task ClearMonolithBasketAsync()
    {
        try
        {
            var username = User.Identity?.Name;

            if (string.IsNullOrEmpty(username))
            {
                return;
            }

            var basket = await _basketViewModelService.GetOrCreateBasketForUser(username);

            if (basket.Items.Count > 0)
            {
                await _basketService.DeleteBasketAsync(basket.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not clear the basket in the monolith after a completed checkout");
        }
    }
}

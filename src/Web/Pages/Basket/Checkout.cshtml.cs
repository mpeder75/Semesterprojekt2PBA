using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Web.Configuration;
using Microsoft.eShopWeb.Web.Interfaces;
using Microsoft.eShopWeb.Web.ViewModels;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Web.Pages.Basket;

[Authorize]
public class CheckoutModel : PageModel
{
    private readonly IBasketService _basketService;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IOrderService _orderService;
    private string? _username = null;
    private readonly IBasketViewModelService _basketViewModelService;
    private readonly IAppLogger<CheckoutModel> _logger;
    private readonly ICheckoutApiClient _checkoutApiClient;
    private readonly IBasketApiClient _basketApiClient;
    private readonly CheckoutServiceSettings _checkoutSettings;

    public CheckoutModel(IBasketService basketService,
        IBasketViewModelService basketViewModelService,
        SignInManager<ApplicationUser> signInManager,
        IOrderService orderService,
        IAppLogger<CheckoutModel> logger,
        ICheckoutApiClient checkoutApiClient,
        IBasketApiClient basketApiClient,
        IOptions<CheckoutServiceSettings> checkoutSettings)
    {
        _basketService = basketService;
        _signInManager = signInManager;
        _orderService = orderService;
        _basketViewModelService = basketViewModelService;
        _logger = logger;
        _checkoutApiClient = checkoutApiClient;
        _basketApiClient = basketApiClient;
        _checkoutSettings = checkoutSettings.Value;
    }

    public BasketViewModel BasketModel { get; set; } = new BasketViewModel();

    /// <summary>
    /// Klientens Idempotency-Key til checkouten. Oprettes, når siden vises, og sendes med som skjult felt, så en
    /// dobbeltklik eller et genforsøg på samme side giver den samme checkout og ikke to.
    /// </summary>
    [BindProperty]
    public Guid CheckoutKey { get; set; }

    public async Task OnGet()
    {
        CheckoutKey = Guid.NewGuid();
        await SetBasketModelAsync();
    }

    public async Task<IActionResult> OnPost(IEnumerable<BasketItemViewModel> items)
    {
        try
        {
            await SetBasketModelAsync();

            if (!ModelState.IsValid)
            {
                return BadRequest();
            }

            var updateModel = items.ToDictionary(b => b.Id.ToString(), b => b.Quantity);
            await _basketService.SetQuantities(BasketModel.Id, updateModel);

            if (_checkoutSettings.UseOrderService)
            {
                _logger.LogInformation("Checkout: starting at OrderService for basket {BasketId}", BasketModel.Id);
                return await StartCheckoutAtOrderServiceAsync();
            }

            _logger.LogInformation("Checkout: using the monolith's own checkout for basket {BasketId} (CheckoutService:UseOrderService is off)", BasketModel.Id);
            await _orderService.CreateOrderAsync(BasketModel.Id, new Address("123 Main St.", "Kent", "OH", "United States", "44240"));
            await _basketService.DeleteBasketAsync(BasketModel.Id);
        }
        catch (EmptyBasketOnCheckoutException emptyBasketOnCheckoutException)
        {
            //Redirect to Empty Basket page
            _logger.LogWarning(emptyBasketOnCheckoutException.Message);
            return RedirectToPage("/Basket/Index");
        }

        return RedirectToPage("Success");
    }

    /// <summary>
    /// Starter checkouten hos OrderService og sender kunden videre til statussiden.
    /// </summary>
    /// <remarks>
    /// Monolitten ejer stadig kurven, men OrderService henter kurvens indhold hos BasketService. Derfor lægges kurven
    /// først over i BasketService (en bro), og det kurv-id, BasketService giver tilbage, bruges til checkouten.
    /// Kurven i monolitten ryddes først, når checkouten er gennemført (se statussiden).
    /// </remarks>
    private async Task<IActionResult> StartCheckoutAtOrderServiceAsync()
    {
        if (BasketModel.Items.Count == 0)
        {
            return RedirectToPage("/Basket/Index");
        }

        // Samme adresse som monolittens checkout bruger i dag (der er endnu ingen adresseformular)
        var address = new CheckoutAddress("123 Main St.", "Kent", "OH", "United States", "44240");
        var buyerId = BasketModel.BuyerId ?? User.Identity!.Name!;
        var checkoutKey = CheckoutKey == Guid.Empty ? Guid.NewGuid() : CheckoutKey;

        int basketServiceBasketId;
        try
        {
            var lines = BasketModel.Items
                .Select(i => new BasketLine(i.CatalogItemId, i.UnitPrice, i.Quantity))
                .ToList();
            basketServiceBasketId = await _basketApiClient.ReplaceBasketAsync(buyerId, lines);
        }
        catch (BasketBridgeException ex)
        {
            _logger.LogWarning("Could not prepare the basket in BasketService: {Message}", ex.Message);
            ModelState.AddModelError(string.Empty, "We could not prepare your basket for checkout right now. Please try again.");
            CheckoutKey = checkoutKey;
            return Page();
        }

        var result = await _checkoutApiClient.StartAsync(checkoutKey, basketServiceBasketId, buyerId, address);

        switch (result.Outcome)
        {
            case StartCheckoutOutcome.Accepted:
                return RedirectToPage("CheckoutProcessing", new { checkoutId = checkoutKey });

            case StartCheckoutOutcome.Conflict:
                ModelState.AddModelError(string.Empty, "A checkout is already in progress for your basket. Please wait for it to finish.");
                break;

            default:
                _logger.LogWarning("Checkout could not be started: {Outcome} {Message}", result.Outcome, result.Message);
                ModelState.AddModelError(string.Empty, "We could not start your checkout right now. Please try again.");
                break;
        }

        CheckoutKey = checkoutKey;
        return Page();
    }

    private async Task SetBasketModelAsync()
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));
        if (_signInManager.IsSignedIn(HttpContext.User))
        {
            BasketModel = await _basketViewModelService.GetOrCreateBasketForUser(User.Identity.Name);
        }
        else
        {
            GetOrSetBasketCookieAndUserName();
            BasketModel = await _basketViewModelService.GetOrCreateBasketForUser(_username!);
        }
    }

    private void GetOrSetBasketCookieAndUserName()
    {
        if (Request.Cookies.ContainsKey(Constants.BASKET_COOKIENAME))
        {
            _username = Request.Cookies[Constants.BASKET_COOKIENAME];
        }
        if (_username != null) return;

        _username = Guid.NewGuid().ToString();
        var cookieOptions = new CookieOptions();
        cookieOptions.Expires = DateTime.Today.AddYears(10);
        Response.Cookies.Append(Constants.BASKET_COOKIENAME, _username, cookieOptions);
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Web.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace CatalogServiceTests;

public class HostIntegrationTests
{
    private static void Configure(IWebHostBuilder builder, CatalogFactory catalog)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("UseOnlyInMemoryDatabase", "true");
        builder.UseSetting("FeatureFlags:UseCatalogApi", "true");
        builder.UseSetting("CatalogApi:BaseUrl", "http://catalog.test/");
        builder.UseSetting("CatalogApi:ApiKey", CatalogFactory.TestKey);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["UseOnlyInMemoryDatabase"] = "true",
            ["FeatureFlags:UseCatalogApi"] = "true",
            ["CatalogApi:BaseUrl"] = "http://catalog.test/",
            ["CatalogApi:ApiKey"] = CatalogFactory.TestKey
        }));
        builder.ConfigureTestServices(services =>
        {
            var name = Guid.NewGuid().ToString();
            services.AddScoped(_ => new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(name).Options);
            services.AddScoped(_ => new DbContextOptionsBuilder<AppIdentityDbContext>().UseInMemoryDatabase(name + "-identity").Options);
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.AddLogging(logging => logging.ClearProviders());
            services.AddHttpClient(nameof(ICatalogClient)).ConfigurePrimaryHttpMessageHandler(() => catalog.Server.CreateHandler());
        });
    }

    [Fact]
    public async Task StorefrontBasketAndCheckoutUseRemoteCatalog()
    {
        using var catalog = new CatalogFactory();
        using var web = new WebApplicationFactory<IBasketViewModelService>().WithWebHostBuilder(builder => Configure(builder, catalog));
        using var http = web.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var response = await http.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(".NET Bot Black Sweatshirt", await response.Content.ReadAsStringAsync());

        using var scope = web.Services.CreateScope();
        var basket = await scope.ServiceProvider.GetRequiredService<IBasketService>().AddItemToBasket("remote-buyer", 1, 19.5m);
        var basketView = await scope.ServiceProvider.GetRequiredService<IBasketViewModelService>().Map(basket);
        Assert.Equal(".NET Bot Black Sweatshirt", Assert.Single(basketView.Items).ProductName);
        await scope.ServiceProvider.GetRequiredService<IOrderService>().CreateOrderAsync(basket.Id,
            new Address("Street", "City", "State", "Country", "1234"));
        var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        Assert.False(await db.CatalogItems.AnyAsync());
        var order = await db.Orders.Include(o => o.OrderItems).SingleAsync(o => o.BuyerId == "remote-buyer");
        Assert.Equal(".NET Bot Black Sweatshirt", Assert.Single(order.OrderItems).ItemOrdered.ProductName);
    }

    [Fact]
    public async Task DeletedProductStaysVisibleAsUnavailableAndCannotBecomeAnOrder()
    {
        using var catalog = new CatalogFactory();
        using var web = new WebApplicationFactory<IBasketViewModelService>().WithWebHostBuilder(builder => Configure(builder, catalog));
        using var http = web.CreateClient();
        using var scope = web.Services.CreateScope();
        var baskets = scope.ServiceProvider.GetRequiredService<IBasketService>();
        var basket = await baskets.AddItemToBasket("deleted-product-buyer", 1, 19.5m);
        var orders = scope.ServiceProvider.GetRequiredService<IOrderService>();
        var address = new Address("Street", "City", "State", "Country", "1234");
        await orders.CreateOrderAsync(basket.Id, address);
        using var remote = catalog.AuthorizedClient();
        Assert.Equal(HttpStatusCode.NoContent, (await remote.DeleteAsync("/catalog/items/1")).StatusCode);

        var view = await scope.ServiceProvider.GetRequiredService<IBasketViewModelService>().Map(basket);
        var item = Assert.Single(view.Items);
        Assert.False(item.IsAvailable);
        Assert.Equal(1, item.CatalogItemId);
        Assert.Equal("Unavailable product", item.ProductName);
        await Assert.ThrowsAsync<Microsoft.eShopWeb.ApplicationCore.Exceptions.UnavailableBasketItemsException>(
            () => orders.CreateOrderAsync(basket.Id, address));
        var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var saved = Assert.Single(await db.Orders.Include(o => o.OrderItems).ToListAsync());
        Assert.Equal(".NET Bot Black Sweatshirt", Assert.Single(saved.OrderItems).ItemOrdered.ProductName);
        Assert.Equal(19.5m, Assert.Single(saved.OrderItems).UnitPrice);
        var cleared = await baskets.SetQuantities(basket.Id, new Dictionary<string, int> { [item.Id.ToString()] = 0 });
        Assert.Empty(cleared.Value.Items);
    }

    [Fact]
    public async Task PublicApiKeepsAdminAuthorizationAndRoutesWritesToCatalog()
    {
        using var catalog = new CatalogFactory();
        using var api = new WebApplicationFactory<Microsoft.eShopWeb.PublicApi.MappingProfile>()
            .WithWebHostBuilder(builder => Configure(builder, catalog));
        using var http = api.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var body = new { Name = "Remote admin product", Description = "Test", Price = 10, CatalogBrandId = 1, CatalogTypeId = 1 };
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync("/api/catalog-items", body)).StatusCode);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("User"));
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsJsonAsync("/api/catalog-items", body)).StatusCode);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS));
        Assert.Equal(HttpStatusCode.Created, (await http.PostAsJsonAsync("/api/catalog-items", body)).StatusCode);
        using var remoteHttp = catalog.AuthorizedClient();
        var remote = new Microsoft.eShopWeb.Infrastructure.Services.HttpCatalogClient(remoteHttp);
        Assert.Contains(await remote.GetItemsByIdsAsync([13]), item => item.Name == body.Name);
        using var scope = api.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<CatalogContext>().CatalogItems.AnyAsync());
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/catalog-items?pageSize=2")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/catalog-brands")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/catalog-types")).StatusCode);
    }

    private static string Token(string role)
    {
        var key = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(Microsoft.eShopWeb.ApplicationCore.Constants.AuthorizationConstants.JWT_SECRET_KEY));
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            claims: [new Claim(ClaimTypes.Name, "test-admin"), new Claim(ClaimTypes.Role, role)],
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)));
    }
}

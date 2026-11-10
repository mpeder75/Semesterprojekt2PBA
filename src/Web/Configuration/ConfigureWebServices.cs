using MediatR;
using Microsoft.eShopWeb.Web.Interfaces;
using Microsoft.eShopWeb.Web.Services;

namespace Microsoft.eShopWeb.Web.Configuration;

public static class ConfigureWebServices
{
    public static IServiceCollection AddWebServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMediatR(cfg => 
            cfg.RegisterServicesFromAssembly(typeof(BasketViewModelService).Assembly));
        services.AddScoped<IBasketViewModelService, BasketViewModelService>();
        services.AddScoped<CatalogViewModelService>();
        services.AddScoped<ICatalogItemViewModelService, CatalogItemViewModelService>();
        services.Configure<CatalogSettings>(configuration);
        services.AddScoped<ICatalogViewModelService, CachedCatalogViewModelService>();

        // Checkout via OrderService. BaseUrl er gatewayens adresse (eller OrderService direkte under udvikling).
        // UseOrderService er slået fra som standard, så monolittens egen checkout bruges, indtil hele kæden er koblet på.
        services.Configure<CheckoutServiceSettings>(configuration.GetSection(CheckoutServiceSettings.SectionName));
        services.AddHttpClient<ICheckoutApiClient, CheckoutApiClient>(client =>
        {
            var baseUrl = configuration[$"{CheckoutServiceSettings.SectionName}:BaseUrl"];
            client.BaseAddress = new Uri(string.IsNullOrWhiteSpace(baseUrl) ? "http://localhost:5233/" : baseUrl);
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        return services;
    }
}

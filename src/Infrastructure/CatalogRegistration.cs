using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure;

public static class CatalogRegistration
{
    public static IServiceCollection AddCatalogClient(this IServiceCollection services, IConfiguration configuration)
    {
        // Restart both hosts to switch. All reads and writes use one backend per process.
        if (!configuration.GetValue<bool>("FeatureFlags:UseCatalogApi"))
        {
            services.AddScoped<ICatalogClient, RepositoryCatalogClient>();
            return services;
        }

        if (!Uri.TryCreate(configuration["CatalogApi:BaseUrl"], UriKind.Absolute, out var address) ||
            (address.Scheme != "http" && address.Scheme != "https"))
            throw new InvalidOperationException("CatalogApi:BaseUrl must be an absolute HTTP(S) URL.");
        var key = configuration["CatalogApi:ApiKey"];
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("CatalogApi:ApiKey is required when UseCatalogApi is enabled.");
        var timeout = configuration.GetValue<int?>("CatalogApi:TimeoutSeconds") ?? 5;
        if (timeout <= 0 || timeout > 120) throw new InvalidOperationException("CatalogApi:TimeoutSeconds must be between 1 and 120.");

        services.AddHttpClient<ICatalogClient, HttpCatalogClient>(http =>
        {
            http.BaseAddress = new Uri(address.AbsoluteUri.TrimEnd('/') + "/");
            http.Timeout = TimeSpan.FromSeconds(timeout);
            http.DefaultRequestHeaders.Add("X-Catalog-Key", key);
        });
        return services;
    }
}

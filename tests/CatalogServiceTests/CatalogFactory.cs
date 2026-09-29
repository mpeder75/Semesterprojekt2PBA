using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Catalog.Api;
using Microsoft.eShopWeb.Catalog.Api.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace CatalogServiceTests;

public class CatalogFactory : WebApplicationFactory<CatalogApiMarker>
{
    public const string TestKey = "catalog-test-key";
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CatalogApi:ApiKey"] = TestKey,
            ["UseOnlyInMemoryDatabase"] = "true",
            ["SeedDemoData"] = "true"
        }));
        builder.ConfigureServices(services =>
        {
            services.AddLogging(logging => logging.ClearProviders());
            services.RemoveAll<DbContextOptions<CatalogDbContext>>();
            var name = Guid.NewGuid().ToString();
            services.AddDbContext<CatalogDbContext>(options => options.UseInMemoryDatabase(name));
        });
    }

    public HttpClient AuthorizedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Catalog-Key", TestKey);
        return client;
    }
}

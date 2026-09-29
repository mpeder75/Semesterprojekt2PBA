using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Catalog.Contracts;
using Microsoft.eShopWeb.Infrastructure;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CatalogServiceTests;

public class ToggleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ToggleRoutesAllOperationsToSelectedBackend(bool remote)
    {
        using var factory = new CatalogFactory();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<CatalogContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        services.AddScoped(typeof(IReadRepository<>), typeof(EfRepository<>));
        services.AddCatalogClient(Config(remote));
        services.AddHttpClient(nameof(ICatalogClient)).ConfigurePrimaryHttpMessageHandler(() => factory.Server.CreateHandler());
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        await CatalogContextSeed.SeedAsync(db, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        var client = scope.ServiceProvider.GetRequiredService<ICatalogClient>();
        Assert.Equal(remote ? typeof(HttpCatalogClient) : typeof(RepositoryCatalogClient), client.GetType());
        var created = await client.CreateItemAsync(new CatalogItemWrite("Toggle product", "Test", 10, 1, 1));
        Assert.Equal("Toggle product", (await client.GetItemByIdAsync(created.Id))!.Name);
        Assert.Single(await client.GetItemsByIdsAsync([created.Id]));
        Assert.Equal(13, (await client.GetItemsAsync(0, 0, null, null)).TotalItems);
        Assert.Equal(5, (await client.GetBrandsAsync()).Count);
        Assert.Equal(4, (await client.GetTypesAsync()).Count);
        Assert.Equal(11, (await client.UpdateItemAsync(created.Id, new CatalogItemWrite("Toggle product", "Test", 11, 1, 1)))!.Price);
        Assert.Equal(!remote, await db.CatalogItems.AnyAsync(i => i.Name == "Toggle product"));
        Assert.True(await client.DeleteItemAsync(created.Id));
    }

    [Fact]
    public async Task RemoteFailureDoesNotFallBackToLocalData()
    {
        var services = new ServiceCollection();
        services.AddCatalogClient(Config(true));
        services.AddHttpClient(nameof(ICatalogClient)).ConfigurePrimaryHttpMessageHandler(() => new FailingHandler());
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ICatalogClient>();
        await Assert.ThrowsAsync<CatalogUnavailableException>(() => client.GetItemByIdAsync(1));
    }

    [Fact]
    public void RemoteModeRequiresConfigurationButLocalModeDoesNot()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["FeatureFlags:UseCatalogApi"] = "true" }).Build();
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddCatalogClient(configuration));
        new ServiceCollection().AddCatalogClient(new ConfigurationBuilder().Build());
    }

    public static IConfiguration Config(bool remote) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["FeatureFlags:UseCatalogApi"] = remote.ToString(),
        ["CatalogApi:BaseUrl"] = "http://catalog.test/",
        ["CatalogApi:ApiKey"] = CatalogFactory.TestKey
    }).Build();

    private class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}

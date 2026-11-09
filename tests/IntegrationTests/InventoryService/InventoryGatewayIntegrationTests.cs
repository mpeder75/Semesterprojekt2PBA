extern alias GatewayApi;

#nullable enable

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;
using Yarp.ReverseProxy.Forwarder;

namespace Microsoft.eShopWeb.IntegrationTests.InventoryService;

public class InventoryGatewayIntegrationTests(InventoryApiFactory inventory) : IClassFixture<InventoryApiFactory>
{
    [Fact]
    public async Task Gateway_ForwardsCrudAndReservationFlowToInventoryAndSqlServer()
    {
        using var gateway = new InventoryGatewayFactory(inventory);
        using var client = gateway.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"), AllowAutoRedirect = false
        });

        using var created = await client.PostAsJsonAsync("/api/inventory", new { catalogItemId = 1, quantity = 10 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("/api/inventory/1", created.Headers.Location?.ToString());

        var stock = await client.GetFromJsonAsync<Stock>("/api/inventory/1");
        Assert.Equal(new Stock(1, 10, 0, 10), stock);
        using var adjusted = await client.PostAsJsonAsync("/api/inventory/1/adjustments", new { quantityChange = 2 });
        Assert.Equal(HttpStatusCode.OK, adjusted.StatusCode);

        var request = new { orderId = 42, items = new[] { new { catalogItemId = 1, quantity = 3 } } };
        using var reserved = await client.PostAsJsonAsync("/api/inventory/reservations", request);
        Assert.Equal(HttpStatusCode.Created, reserved.StatusCode);
        using var replay = await client.PostAsJsonAsync("/api/inventory/reservations", request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(new Stock(1, 12, 3, 9), await client.GetFromJsonAsync<Stock>("/api/inventory/1"));

        using var invalid = await client.PutAsJsonAsync("/api/inventory/1", new { quantity = 2 });
        Assert.Equal(HttpStatusCode.Conflict, invalid.StatusCode);
        using var reservation = await client.GetAsync("/api/inventory/reservations/42");
        Assert.Equal(HttpStatusCode.OK, reservation.StatusCode);
        using var release = await client.PostAsync("/api/inventory/reservations/42/release", null);
        Assert.Equal(HttpStatusCode.OK, release.StatusCode);
        Assert.Equal(new Stock(1, 12, 0, 12), await client.GetFromJsonAsync<Stock>("/api/inventory/1"));

        using var emptied = await client.PutAsJsonAsync("/api/inventory/1", new { quantity = 0 });
        Assert.Equal(HttpStatusCode.OK, emptied.StatusCode);
        using var deleted = await client.DeleteAsync("/api/inventory/1");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var missing = await client.GetAsync("/api/inventory/1");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private sealed record Stock(int CatalogItemId, int Quantity, int ReservedQuantity, int AvailableQuantity);

    private sealed class InventoryGatewayFactory : WebApplicationFactory<GatewayApi::Program>
    {
        private readonly InventoryApiFactory _inventory;
        private readonly Dictionary<string, string?> _settings = new()
        {
            ["AllowedHosts"] = "localhost",
            ["FeatureManagement:UseCatalogService"] = "false",
            ["FeatureManagement:UseBasketService"] = "false",
            ["FeatureManagement:UseOrderService"] = "false",
            ["FeatureManagement:UseInventoryService"] = "true",
            ["Logging:LogLevel:Default"] = "Error"
        };

        public InventoryGatewayFactory(InventoryApiFactory inventory)
        {
            _inventory = inventory;
            _ = inventory.Server;
            foreach (var cluster in new[]
                { "monolith-api", "monolith-web", "catalog-service", "basket-service", "order-service", "inventory-service" })
            {
                // Transport below sends YARP requests into the real Inventory TestServer.
                _settings[$"ReverseProxy:Clusters:{cluster}:Destinations:test:Address"] = "http://inventory.test/";
            }
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(_settings));
            return base.CreateHost(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(_settings));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IForwarderHttpClientFactory>();
                services.AddSingleton<IForwarderHttpClientFactory>(new InventoryTransport(_inventory));
            });
        }
    }

    private sealed class InventoryTransport(InventoryApiFactory inventory) : IForwarderHttpClientFactory
    {
        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) =>
            new(inventory.Server.CreateHandler());
    }
}

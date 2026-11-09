#nullable enable

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.InventoryService;

public class InventoryApiTests : IClassFixture<InventoryApiFactory>, IDisposable
{
    private readonly InventoryApiFactory _factory;
    private readonly HttpClient _client;

    public InventoryApiTests(InventoryApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateAdminClient();
    }

    [Fact]
    public async Task InitializeReadAndUpdateStock_UsesTheHttpContract()
    {
        var created = await _client.PostAsJsonAsync("/api/inventory", new { catalogItemId = 1, quantity = 10 });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("/api/inventory/1", created.Headers.Location?.ToString());
        Assert.Equal(new StockResponse(1, 10), await created.Content.ReadFromJsonAsync<StockResponse>());
        Assert.Equal(new StockResponse(1, 10), await _client.GetFromJsonAsync<StockResponse>("/api/inventory/1"));

        var updated = await _client.PutAsJsonAsync("/api/inventory/1", new { quantity = 7 });

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(new StockResponse(1, 7), await updated.Content.ReadFromJsonAsync<StockResponse>());
        using var anonymousClient = _factory.CreateClient();
        Assert.Equal(new StockResponse(1, 7), await anonymousClient.GetFromJsonAsync<StockResponse>("/api/inventory/1"));
    }

    [Theory]
    [InlineData(null, true, HttpStatusCode.Unauthorized)]
    [InlineData("Customers", true, HttpStatusCode.Forbidden)]
    [InlineData("Administrators", false, HttpStatusCode.Unauthorized)]
    public async Task InventoryWrites_RequireATrustedAdministrator(
        string? role, bool validSignature, HttpStatusCode expectedStatus)
    {
        using var client = _factory.CreateClient();
        if (role is not null)
            client.DefaultRequestHeaders.Authorization = InventoryApiFactory.CreateAuthorization(role, validSignature);

        var requests = new (HttpMethod Method, string Path, object? Body)[]
        {
            (HttpMethod.Post, "/api/inventory", new { catalogItemId = 900, quantity = 10 }),
            (HttpMethod.Put, "/api/inventory/900", new { quantity = 12 }),
            (HttpMethod.Post, "/api/inventory/900/adjustments", new { quantityChange = 2 }),
            (HttpMethod.Delete, "/api/inventory/900", null),
            (HttpMethod.Post, "/api/inventory/reservations", new
            {
                orderId = 900, items = new[] { new { catalogItemId = 900, quantity = 3 } }
            }),
            (HttpMethod.Post, "/api/inventory/reservations/900/confirm", null),
            (HttpMethod.Post, "/api/inventory/reservations/900/release", null)
        };

        foreach (var (method, path, body) in requests)
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null)
                request.Content = JsonContent.Create(body);

            using var response = await client.SendAsync(request);
            Assert.Equal(expectedStatus, response.StatusCode);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(int.MaxValue)]
    public async Task SetQuantity_AcceptsNonNegativeIntegerLimits(int quantity)
    {
        var productId = quantity == 0 ? 2 : 3;

        var response = await _client.PostAsJsonAsync("/api/inventory", new { catalogItemId = productId, quantity });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(new StockResponse(productId, quantity), await response.Content.ReadFromJsonAsync<StockResponse>());
    }

    [Fact]
    public async Task GetUninitializedStock_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/inventory/999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task InvalidProductId_ReturnsBadRequest(int productId)
    {
        var get = await _client.GetAsync($"/api/inventory/{productId}");
        var put = await _client.PutAsJsonAsync($"/api/inventory/{productId}", new { quantity = 5 });

        Assert.Equal(HttpStatusCode.BadRequest, get.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Theory]
    [InlineData("{\"quantity\":-1}")]
    [InlineData("{}")]
    [InlineData("{\"quantity\":null}")]
    [InlineData("{\"quantity\":\"invalid\"}")]
    [InlineData("{\"quantity\":1.5}")]
    [InlineData("{\"quantity\":2147483648}")]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("")]
    public async Task InvalidBody_ReturnsBadRequestAndDoesNotInitializeStock(string json)
    {
        using var body = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _client.PutAsync("/api/inventory/100", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/inventory/100")).StatusCode);
    }

    [Fact]
    public async Task InvalidUpdate_PreservesTheStoredQuantity()
    {
        var initialized = await _client.PostAsJsonAsync("/api/inventory", new { catalogItemId = 4, quantity = 10 });
        Assert.Equal(HttpStatusCode.Created, initialized.StatusCode);

        var invalid = await _client.PutAsJsonAsync("/api/inventory/4", new { quantity = -1 });

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(new StockResponse(4, 10), await _client.GetFromJsonAsync<StockResponse>("/api/inventory/4"));
    }

    [Fact]
    public async Task RepeatingPut_UpdatesOneStockRecord()
    {
        Assert.Equal(HttpStatusCode.Created,
            (await _client.PostAsJsonAsync("/api/inventory", new { catalogItemId = 5, quantity = 12 })).StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await _client.PutAsJsonAsync("/api/inventory/5", new { quantity = 12 })).StatusCode);
        Assert.Equal(new StockResponse(5, 12), await _client.GetFromJsonAsync<StockResponse>("/api/inventory/5"));
    }

    [Fact]
    public async Task StockSurvivesApplicationRestart()
    {
        Assert.Equal(HttpStatusCode.Created,
            (await _client.PostAsJsonAsync("/api/inventory", new { catalogItemId = 6, quantity = 8 })).StatusCode);

        await using var restarted = _factory.WithWebHostBuilder(_ => { });
        using var client = restarted.CreateClient();

        Assert.Equal(new StockResponse(6, 8), await client.GetFromJsonAsync<StockResponse>("/api/inventory/6"));
    }

    [Fact]
    public async Task ConcurrentCreation_CreatesOneRecordAndRejectsDuplicates()
    {
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            _client.PostAsJsonAsync("/api/inventory", new { catalogItemId = 7, quantity = 10 })));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.All(responses, response => Assert.Contains(response.StatusCode,
            new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }));
        Assert.Equal(new StockResponse(7, 10), await _client.GetFromJsonAsync<StockResponse>("/api/inventory/7"));
    }

    [Theory]
    [InlineData(200, -1)]
    [InlineData(0, 10)]
    public async Task DatabaseConstraints_RejectInvalidStock(int productId, int quantity)
    {
        // Exercise SQL Server's constraints independently of HTTP/entity validation.
        await using var connection = new SqlConnection(_factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO InventoryItems (CatalogItemId, Quantity) VALUES (@id, @quantity)";
        command.Parameters.AddWithValue("@id", productId);
        command.Parameters.AddWithValue("@quantity", quantity);

        var error = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());

        Assert.Equal(547, error.Number);
    }

    private sealed record StockResponse(int CatalogItemId, int Quantity);

    public void Dispose() => _client.Dispose();
}

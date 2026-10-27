using System.Net;
using ApiGateway.Tests.Support;

namespace ApiGateway.Tests;

public sealed class TrafficControlTests
{
    [Fact]
    public async Task Api_allowance_is_shared_across_all_service_routes()
    {
        using var factory = new GatewayFactory(new GatewayOptions
        {
            Catalog = true, Basket = true, Order = true,
            Configuration = Limits(api: 1)
        });
        using var client = factory.CreateGatewayClient();

        Assert.Equal(HttpStatusCode.OK, await Get(client, "/api/catalog-items"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await Get(client, "/api/orders"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await Get(client, "/api/baskets"));
        Assert.Single(factory.Requests);
    }

    [Fact]
    public async Task Forwarded_clients_have_independent_api_allowances()
    {
        using var factory = new GatewayFactory(new GatewayOptions
        {
            Catalog = true,
            Configuration = Limits(api: 1)
        });
        using var client = factory.CreateGatewayClient();

        Assert.Equal(HttpStatusCode.OK, await Get(client, "/api/catalog-items", "203.0.113.10"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await Get(client, "/api/catalog-items", "203.0.113.10"));
        Assert.Equal(HttpStatusCode.OK, await Get(client, "/api/catalog-items", "203.0.113.20"));
        Assert.Equal(HttpStatusCode.TooManyRequests,
            await Get(client, "/api/catalog-items", "203.0.113.99, 203.0.113.20"));
        Assert.Equal(2, factory.Requests.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Untrusted_clients_cannot_change_their_allowance_with_a_forged_header(bool azure)
    {
        using var factory = new GatewayFactory(new GatewayOptions
        {
            Catalog = true,
            AzureAppService = azure,
            PeerAddress = "198.51.100.7",
            Configuration = Limits(api: 1)
        });
        using var client = factory.CreateGatewayClient();
        Assert.Equal(HttpStatusCode.OK, await Get(client, "/api/catalog-items", "203.0.113.10"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await Get(client, "/api/catalog-items", "203.0.113.20"));
        Assert.Single(factory.Requests);
    }

    [Fact]
    public async Task Both_login_posts_share_a_budget_separate_from_the_api_budget()
    {
        using var factory = new GatewayFactory(new GatewayOptions
        {
            Catalog = true,
            Configuration = Limits(api: 1, login: 1)
        });
        using var client = factory.CreateGatewayClient();

        Assert.Equal(HttpStatusCode.OK, await Get(client, "/api/catalog-items"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await Get(client, "/api/catalog-brands"));
        using var firstLogin = await client.PostAsync("/api/authenticate", new StringContent("test"));
        using var secondLogin = await client.PostAsync("/Identity/Account/Login", new StringContent("test"));
        Assert.Equal(HttpStatusCode.OK, firstLogin.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, secondLogin.StatusCode);

        // Login pages, ordinary Web pages, and liveness stay available.
        Assert.Equal(HttpStatusCode.OK, await Get(client, "/Identity/Account/Login"));
        Assert.Equal(HttpStatusCode.OK, await Get(client, "/User"));
        Assert.Equal(HttpStatusCode.OK, await Get(client, "/health/gateway"));
        Assert.Equal(4, factory.Requests.Count);
    }

    [Fact]
    public async Task Login_allowances_are_partitioned_by_forwarded_client_ip()
    {
        using var factory = new GatewayFactory(new GatewayOptions
        {
            Configuration = Limits(login: 1)
        });
        using var client = factory.CreateGatewayClient();
        Assert.Equal(HttpStatusCode.OK, await Login(client, "203.0.113.10"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await Login(client, "203.0.113.10"));
        Assert.Equal(HttpStatusCode.OK, await Login(client, "203.0.113.20"));
        Assert.Equal(2, factory.Requests.Count);
    }

    [Theory]
    [InlineData("ApiPermitLimit", 0)]
    [InlineData("ApiPermitLimit", -1)]
    [InlineData("LoginPermitLimit", 0)]
    [InlineData("LoginPermitLimit", -1)]
    public void Nonpositive_rate_limits_stop_startup(string setting, int value)
    {
        using var factory = new GatewayFactory(new GatewayOptions
        {
            Configuration = new Dictionary<string, string?>
            {
                [$"Gateway:RateLimiting:{setting}"] = value.ToString()
            }
        });
        var error = Assert.ThrowsAny<Exception>(() => factory.CreateGatewayClient());
        Assert.Contains($"{setting} must be greater than zero", error.ToString());
    }

    private static Dictionary<string, string?> Limits(int api = 60, int login = 10) => new()
    {
        ["Gateway:RateLimiting:ApiPermitLimit"] = api.ToString(),
        ["Gateway:RateLimiting:LoginPermitLimit"] = login.ToString()
    };

    private static async Task<HttpStatusCode> Get(HttpClient client, string path, string? address = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (address is not null)
            request.Headers.Add("X-Forwarded-For", address);
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    private static async Task<HttpStatusCode> Login(HttpClient client, string address)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/authenticate")
        {
            Content = new StringContent("test")
        };
        request.Headers.Add("X-Forwarded-For", address);
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }
}

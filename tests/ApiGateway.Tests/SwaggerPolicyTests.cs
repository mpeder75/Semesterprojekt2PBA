using System.Net;
using System.Net.Http.Json;
using ApiGateway.Tests.Support;

namespace ApiGateway.Tests;

public sealed class SwaggerPolicyTests
{
    public static IEnumerable<object[]> SwaggerPaths()
    {
        foreach (var path in new[]
        {
            "/swagger",
            "/swagger/",
            "/swagger/index.html",
            "/swagger/swagger-ui.css",
            "/swagger/v1/swagger.json?cacheBust=1",
            "/SWAGGER/v1/swagger.json"
        })
            yield return new object[] { path };
    }

    public static IEnumerable<object[]> BlockedSwaggerRequests()
    {
        foreach (var environment in new[] { "Production", "Staging", "Testing" })
        foreach (var path in SwaggerPaths())
            yield return new object[] { environment, path[0] };
    }

    [Theory]
    [MemberData(nameof(BlockedSwaggerRequests))]
    public async Task Swagger_is_blocked_outside_development_even_with_an_available_backend(
        string environment, string path)
    {
        // The controlled legacy backend would return 200 if the guard let the
        // request through. A 404 must come from the gateway, not backend failure.
        using var factory = CreateFactory(environment);
        using var client = factory.CreateGatewayClient(https: true);
        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(factory.Requests);
        if (environment == "Production")
            Assert.True(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Theory]
    [MemberData(nameof(SwaggerPaths))]
    public async Task Development_forwards_swagger_to_the_legacy_api(string path)
    {
        using var factory = CreateFactory("Development");
        using var client = factory.CreateGatewayClient(https: true);
        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var forwarded = await response.Content.ReadFromJsonAsync<ForwardedRequest>();
        var expectedUri = new Uri(client.BaseAddress!, path);
        Assert.Equal("monolith-api", forwarded!.Cluster);
        Assert.Equal(expectedUri.AbsolutePath, forwarded.Path);
        Assert.Equal(expectedUri.Query, forwarded.Query);
        Assert.Single(factory.Requests);
    }

    [Theory]
    [InlineData("HEAD")]
    [InlineData("POST")]
    [InlineData("OPTIONS")]
    public async Task Other_http_methods_cannot_bypass_the_production_guard(string method)
    {
        using var factory = CreateFactory("Production");
        using var client = factory.CreateGatewayClient(https: true);
        using var request = new HttpRequestMessage(new HttpMethod(method), "/swagger/v1/swagger.json");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(factory.Requests);
    }

    [Theory]
    [InlineData("/swagger-extra")]
    [InlineData("/swagger.json")]
    [InlineData("/SwaggerHelp/index.html")]
    public async Task Similar_web_paths_are_not_blocked(string path)
    {
        using var factory = CreateFactory("Production");
        using var client = factory.CreateGatewayClient(https: true);
        await ServiceRoutingTests.AssertForwarded(client, path, "monolith-web", path);
        Assert.Single(factory.Requests);
    }

    [Theory]
    [InlineData("/swagger")]
    [InlineData("/swagger/v1/swagger.json")]
    public async Task Actual_production_configuration_returns_404_when_public_api_is_unavailable(
        string path)
    {
        using var factory = new GatewayFactory(new GatewayOptions
        {
            Catalog = true,
            Environment = "Production",
            UseConfiguredDestinations = true
        });
        using var client = factory.CreateGatewayClient(https: true);
        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(factory.Requests);
    }

    private static GatewayFactory CreateFactory(string environment) => new(new GatewayOptions
    {
        Catalog = true,
        Environment = environment
    });
}

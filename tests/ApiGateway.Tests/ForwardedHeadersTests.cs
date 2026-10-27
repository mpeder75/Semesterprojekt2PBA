using System.Net;
using System.Net.Http.Json;
using ApiGateway.Tests.Support;

namespace ApiGateway.Tests;

public sealed class ForwardedHeadersTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    public async Task Trusted_loopback_proxy_supplies_the_client_ip_and_https_scheme(string peer)
    {
        using var factory = new GatewayFactory(new GatewayOptions { Catalog = true, PeerAddress = peer });
        using var client = factory.CreateGatewayClient();
        var forwarded = await Forward(client, "203.0.113.10", "https");
        Assert.Contains("203.0.113.10", forwarded.ForwardedFor);
        Assert.Equal("https", forwarded.ForwardedProto);
        Assert.Equal("localhost", forwarded.ForwardedHost);
    }

    [Theory]
    [InlineData("10.20.30.40")]
    [InlineData("172.16.20.30")]
    [InlineData("192.168.20.30")]
    [InlineData("::ffff:10.20.30.40")]
    [InlineData("::ffff:172.16.20.30")]
    [InlineData("::ffff:192.168.20.30")]
    public async Task Azure_proxy_ranges_accept_one_forwarded_hop_in_both_address_formats(string peer)
    {
        using var factory = new GatewayFactory(new GatewayOptions
        {
            Catalog = true, AzureAppService = true, PeerAddress = peer
        });
        using var client = factory.CreateGatewayClient();
        var forwarded = await Forward(client, "203.0.113.10", "https");
        Assert.Contains("203.0.113.10", forwarded.ForwardedFor);
        Assert.Equal("https", forwarded.ForwardedProto);
    }

    [Theory]
    [InlineData("10.20.30.40")]
    [InlineData("172.16.20.30")]
    [InlineData("192.168.20.30")]
    public async Task Private_proxy_ranges_are_not_trusted_outside_app_service(string peer)
    {
        using var factory = new GatewayFactory(new GatewayOptions { Catalog = true, PeerAddress = peer });
        using var client = factory.CreateGatewayClient();
        var forwarded = await Forward(client, "203.0.113.99", "https");
        Assert.Contains(peer, forwarded.ForwardedFor);
        Assert.DoesNotContain("203.0.113.99", forwarded.ForwardedFor);
        Assert.Equal("http", forwarded.ForwardedProto);
    }

    [Theory]
    [InlineData("198.51.100.7")]
    [InlineData("172.32.0.1")]
    [InlineData("192.169.0.1")]
    public async Task Addresses_outside_trusted_networks_cannot_spoof_ip_or_scheme(string peer)
    {
        using var factory = new GatewayFactory(new GatewayOptions
        {
            Catalog = true, AzureAppService = true, PeerAddress = peer
        });
        using var client = factory.CreateGatewayClient();
        var forwarded = await Forward(client, "203.0.113.99", "https");
        Assert.Contains(peer, forwarded.ForwardedFor);
        Assert.DoesNotContain("203.0.113.99", forwarded.ForwardedFor);
        Assert.Equal("http", forwarded.ForwardedProto);
    }

    [Fact]
    public async Task Only_the_nearest_forwarded_hop_is_consumed()
    {
        using var factory = new GatewayFactory(new GatewayOptions { Catalog = true });
        using var client = factory.CreateGatewayClient();
        var forwarded = await Forward(client, "203.0.113.99, 203.0.113.20", "https");
        Assert.Contains("203.0.113.20", forwarded.ForwardedFor);
        Assert.DoesNotContain("203.0.113.99", forwarded.ForwardedFor);
    }

    private static async Task<ForwardedRequest> Forward(HttpClient client, string address, string scheme)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/catalog-items");
        request.Headers.Add("X-Forwarded-For", address);
        request.Headers.Add("X-Forwarded-Proto", scheme);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ForwardedRequest>())!;
    }
}

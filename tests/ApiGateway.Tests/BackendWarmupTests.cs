using System.Collections.Concurrent;
using System.Net;
using ApiGateway.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace ApiGateway.Tests;

public class BackendWarmupTests
{
    [Fact]
    public async Task Azure_startup_warms_only_routed_backends_without_waiting_for_them()
    {
        var expectedHosts = new[] { "basket.test", "catalog.test", "order.test", "web.test" };
        var requests = new ConcurrentQueue<(string Host, HttpMethod Method)>();
        var allRequestsStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowResponses = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeout = TimeSpan.FromSeconds(10);

        using var factory = new GatewayFactory(new GatewayOptions
        {
            AzureAppService = true,
            Catalog = true,
            Basket = true,
            Order = true,
            EmptyClusters = ["monolith-api"],
            Configuration = new Dictionary<string, string?>
            {
                ["ReverseProxy:Clusters:monolith-web:Destinations:test:Address"] = "https://web.test/",
                ["ReverseProxy:Clusters:catalog-service:Destinations:test:Address"] = "https://catalog.test/",
                ["ReverseProxy:Clusters:basket-service:Destinations:test:Address"] = "https://basket.test/",
                ["ReverseProxy:Clusters:order-service:Destinations:test:Address"] = "https://order.test/",
                ["ReverseProxy:Clusters:inventory-service:Destinations:test:Address"] = "https://inventory.test/"
            },
            WarmupBackend = async (request, cancellationToken) =>
            {
                var host = request.RequestUri!.Host;
                requests.Enqueue((host, request.Method));
                if (requests.Count == expectedHosts.Length)
                {
                    allRequestsStarted.TrySetResult();
                }

                await allowResponses.Task.WaitAsync(cancellationToken);
                if (host == "order.test")
                {
                    throw new HttpRequestException("Backend unavailable");
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        });

        try
        {
            using var client = await Task.Run(() => factory.CreateGatewayClient()).WaitAsync(timeout);
            await allRequestsStarted.Task.WaitAsync(timeout);
            using var response = await client.GetAsync("/health/gateway").WaitAsync(timeout);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            allowResponses.SetResult();
            var warmup = Assert.Single(factory.Services.GetServices<IHostedService>().OfType<BackendWarmup>());
            await warmup.ExecuteTask!.WaitAsync(timeout);

            Assert.Equal(expectedHosts, requests.Select(request => request.Host).Order());
            Assert.All(requests, request => Assert.Equal(HttpMethod.Get, request.Method));
        }
        finally
        {
            allowResponses.TrySetResult();
        }
    }

    [Fact]
    public void Local_startup_skips_backend_warmup()
    {
        using var factory = new GatewayFactory();

        Assert.Empty(factory.Services.GetServices<IHostedService>().OfType<BackendWarmup>());
    }
}

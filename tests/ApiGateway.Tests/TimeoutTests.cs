using System.Net;
using ApiGateway.Middleware;
using ApiGateway.Tests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

namespace ApiGateway.Tests;

public sealed class TimeoutTests
{
    [Theory]
    [InlineData("catalog-items", "/api/catalog-items", false)]
    [InlineData("basket-api", "/api/baskets", false)]
    [InlineData("order-api", "/api/orders", false)]
    [InlineData("api-login", "/api/authenticate", true)]
    [InlineData("web-login", "/Identity/Account/Login", true)]
    public async Task Route_timeout_returns_504_and_cancels_the_downstream_request(
        string route, string path, bool post)
    {
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var factory = new GatewayFactory(new GatewayOptions
        {
            Catalog = true, Basket = true, Order = true,
            Configuration = new Dictionary<string, string?>
            {
                [$"ReverseProxy:Routes:{route}:Timeout"] = "00:00:00.200"
            },
            Backend = async (_, token) =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return new HttpResponseMessage(HttpStatusCode.OK);
                }
                catch (OperationCanceledException)
                {
                    canceled.TrySetResult();
                    throw;
                }
            }
        });
        using var client = factory.CreateGatewayClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        using var response = post
            ? await client.PostAsync(path, new StringContent("test"))
            : await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(factory.Requests);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(502)]
    [InlineData(503)]
    public async Task A_backend_error_response_is_preserved(int status)
    {
        using var factory = new GatewayFactory(new GatewayOptions
        {
            Catalog = true,
            Backend = (_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
            {
                Content = new StringContent("backend error")
            })
        });
        using var client = factory.CreateGatewayClient();
        using var response = await client.GetAsync("/api/catalog-items");
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.Equal("backend error", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_downstream_connection_failure_returns_502()
    {
        using var factory = new GatewayFactory(new GatewayOptions
        {
            Catalog = true,
            Backend = (_, _) => Task.FromException<HttpResponseMessage>(
                new HttpRequestException("Simulated connection failure"))
        });
        using var client = factory.CreateGatewayClient();
        using var response = await client.GetAsync("/api/catalog-items");
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task Client_cancellation_stops_the_downstream_request()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var factory = new GatewayFactory(new GatewayOptions
        {
            Catalog = true,
            Backend = async (_, token) =>
            {
                entered.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return new HttpResponseMessage(HttpStatusCode.OK);
                }
                catch (OperationCanceledException)
                {
                    canceled.TrySetResult();
                    throw;
                }
            }
        });
        using var client = factory.CreateGatewayClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        using var cancellation = new CancellationTokenSource();
        var request = client.GetAsync("/api/catalog-items", cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await request);
        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(factory.Requests);
    }

    // This comparison uses the real YARP/ASP.NET pipeline. It documents why the
    // custom middleware is still needed with the installed YARP version.
    [Theory]
    [InlineData(false, 400)]
    [InlineData(true, 504)]
    public async Task Yarp_cancellation_needs_normalization_to_produce_a_gateway_timeout(
        bool normalizeTimeout, int expectedStatus)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing"
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddRequestTimeouts();
        builder.Services.AddReverseProxy().LoadFromMemory(
            new[]
            {
                new RouteConfig
                {
                    RouteId = "slow", ClusterId = "slow-backend",
                    Match = new RouteMatch { Path = "/slow" },
                    Timeout = TimeSpan.FromMilliseconds(200)
                }
            },
            new[]
            {
                new ClusterConfig
                {
                    ClusterId = "slow-backend",
                    Destinations = new Dictionary<string, DestinationConfig>
                    {
                        ["test"] = new() { Address = "http://127.0.0.1:1/" }
                    }
                }
            });
        builder.Services.RemoveAll<IForwarderHttpClientFactory>();
        builder.Services.AddSingleton<IForwarderHttpClientFactory, SlowClientFactory>();

        await using var app = builder.Build();
        app.UseRouting();
        app.UseRequestTimeouts();
        if (normalizeTimeout)
            app.UseMiddleware<ProxyTimeoutMiddleware>();
        app.MapReverseProxy();
        await app.StartAsync();
        using var client = app.GetTestClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        using var response = await client.GetAsync("/slow");
        Assert.Equal((HttpStatusCode)expectedStatus, response.StatusCode);
    }

    [Theory]
    [InlineData(ForwarderError.RequestCanceled)]
    [InlineData(ForwarderError.RequestBodyCanceled)]
    public async Task An_expired_gateway_deadline_clears_the_unstarted_cancellation_response(
        ForwarderError error)
    {
        using var deadline = new CancellationTokenSource();
        var context = CreateContext(deadline.Token, error);
        var middleware = CreateMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 400;
            ctx.Response.Headers["X-Stale-Header"] = "discard me";
            ctx.Response.ContentLength = 100;
            deadline.Cancel();
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);
        Assert.Equal(504, context.Response.StatusCode);
        Assert.Empty(context.Response.Headers);
        Assert.Null(context.Response.ContentLength);
    }

    [Theory]
    [InlineData(ForwarderError.RequestCanceled)]
    [InlineData(ForwarderError.RequestBodyCanceled)]
    public async Task Client_cancellation_is_not_relabelled_as_a_gateway_timeout(ForwarderError error)
    {
        using var clientCancellation = new CancellationTokenSource();
        var context = CreateContext(CancellationToken.None, error);
        context.RequestAborted = clientCancellation.Token;
        var middleware = CreateMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 400;
            clientCancellation.Cancel();
            return Task.CompletedTask;
        });
        await middleware.InvokeAsync(context);
        Assert.Equal(400, context.Response.StatusCode);
    }

    [Theory]
    [InlineData(ForwarderError.RequestCanceled)]
    [InlineData(ForwarderError.RequestBodyCanceled)]
    public async Task A_response_that_has_started_cannot_be_rewritten(ForwarderError error)
    {
        using var deadline = new CancellationTokenSource();
        deadline.Cancel();
        var context = CreateContext(deadline.Token, error);
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());
        var middleware = CreateMiddleware(ctx =>
        {
            ctx.Response.Headers["X-Existing"] = "keep me";
            return Task.CompletedTask;
        });
        await middleware.InvokeAsync(context);
        Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal("keep me", context.Response.Headers["X-Existing"].ToString());
    }

    [Theory]
    [InlineData(ForwarderError.None, 200)]
    [InlineData(ForwarderError.Request, 502)]
    [InlineData(ForwarderError.ResponseHeaders, 502)]
    public async Task A_deadline_does_not_rewrite_unrelated_forwarder_results(
        ForwarderError error, int status)
    {
        using var deadline = new CancellationTokenSource();
        deadline.Cancel();
        var context = CreateContext(deadline.Token, error);
        context.Response.StatusCode = status;
        await CreateMiddleware(_ => Task.CompletedTask).InvokeAsync(context);
        Assert.Equal(status, context.Response.StatusCode);
    }

    [Fact]
    public async Task A_deadline_without_a_forwarder_error_does_not_rewrite_a_backend_response()
    {
        using var deadline = new CancellationTokenSource();
        deadline.Cancel();
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpRequestTimeoutFeature>(new TimeoutFeature(deadline.Token));
        context.Response.StatusCode = 400;
        await CreateMiddleware(_ => Task.CompletedTask).InvokeAsync(context);
        Assert.Equal(400, context.Response.StatusCode);
    }

    private static ProxyTimeoutMiddleware CreateMiddleware(RequestDelegate next)
        => new(next, NullLogger<ProxyTimeoutMiddleware>.Instance);

    private static DefaultHttpContext CreateContext(CancellationToken deadline, ForwarderError error)
    {
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpRequestTimeoutFeature>(new TimeoutFeature(deadline));
        context.Features.Set<IForwarderErrorFeature>(new ErrorFeature(error));
        return context;
    }

    private sealed class TimeoutFeature(CancellationToken token) : IHttpRequestTimeoutFeature
    {
        public CancellationToken RequestTimeoutToken => token;
        public void DisableTimeout() { }
    }

    private sealed class ErrorFeature(ForwarderError error) : IForwarderErrorFeature
    {
        public ForwarderError Error => error;
        public Exception? Exception => null;
    }

    private sealed class StartedResponseFeature : HttpResponseFeature
    {
        public override bool HasStarted => true;
    }

    private sealed class SlowClientFactory : IForwarderHttpClientFactory
    {
        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context)
            => new(new SlowHandler());
    }

    private sealed class SlowHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}

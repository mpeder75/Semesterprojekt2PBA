using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Yarp.ReverseProxy.Forwarder;

namespace ApiGateway.Tests.Support;

internal sealed record GatewayOptions
{
    public bool Catalog { get; init; }
    public bool Basket { get; init; }
    public bool Order { get; init; }
    public string Environment { get; init; } = "Testing";
    public bool UseConfiguredDestinations { get; init; }
    public bool AzureAppService { get; init; }
    public string PeerAddress { get; init; } = "127.0.0.1";
    public IReadOnlyCollection<string> EmptyClusters { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string?> Configuration { get; init; }
        = new Dictionary<string, string?>();
    public Func<ForwardedRequest, CancellationToken, Task<HttpResponseMessage>>? Backend { get; init; }
}

internal sealed record ForwardedRequest(
    string Cluster,
    string Method,
    string Path,
    string Query,
    string? Body,
    string? Authorization,
    string Host,
    string? ForwardedFor,
    string? ForwardedProto,
    string? ForwardedHost);

internal sealed class GatewayFactory : WebApplicationFactory<global::Program>
{
    public static readonly string[] Clusters =
    {
        "monolith-api", "monolith-web", "catalog-service", "basket-service", "order-service"
    };

    private readonly GatewayOptions _options;
    private readonly Dictionary<string, string?> _settings;

    public ConcurrentQueue<ForwardedRequest> Requests { get; } = new();

    public GatewayFactory(GatewayOptions? options = null)
    {
        _options = options ?? new GatewayOptions();
        _settings = new Dictionary<string, string?>
        {
            ["FeatureManagement:UseCatalogService"] = _options.Catalog.ToString(),
            ["FeatureManagement:UseBasketService"] = _options.Basket.ToString(),
            ["FeatureManagement:UseOrderService"] = _options.Order.ToString(),
            ["WEBSITE_SITE_NAME"] = _options.AzureAppService ? "gateway-test-site" : "",
            ["Logging:LogLevel:Default"] = "Error"
        };

        if (!_options.UseConfiguredDestinations)
        {
            foreach (var cluster in Clusters)
            {
                // Keep disabled clusters valid in YARP without providing a destination.
                _settings[$"ReverseProxy:Clusters:{cluster}"] = "";
                if (!_options.EmptyClusters.Contains(cluster))
                {
                    _settings[$"ReverseProxy:Clusters:{cluster}:Destinations:test:Address"]
                        = "http://127.0.0.1:1/";
                }
            }
        }

        foreach (var setting in _options.Configuration)
        {
            _settings[setting.Key] = setting.Value;
        }
    }

    // Some gateway settings are read before builder.Build(). Supply them to the
    // deferred host as well as the final app configuration, without process-wide
    // environment-variable changes that could interfere with parallel tests.
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment(_options.Environment);
        builder.ConfigureHostConfiguration(configuration =>
            configuration.AddInMemoryCollection(_settings));
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(_settings));

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IForwarderHttpClientFactory>();
            services.AddSingleton<IForwarderHttpClientFactory>(
                new StubClientFactory(Requests, _options.Backend));
            services.AddSingleton<IStartupFilter>(
                new PeerAddressStartupFilter(IPAddress.Parse(_options.PeerAddress)));
        });
    }

    public HttpClient CreateGatewayClient(bool https = false, string? host = null)
    {
        host ??= Services.GetRequiredService<IConfiguration>()["AllowedHosts"]!
            .Split(';')[0];
        return CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"{(https ? "https" : "http")}://{host}"),
            AllowAutoRedirect = false,
            HandleCookies = false
        });
    }

    public static string ProjectRoot
    {
        get
        {
            using var manifest = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "MvcTestingAppManifest.json")));
            return manifest.RootElement
                .GetProperty(typeof(global::Program).Assembly.FullName!).GetString()!;
        }
    }

    private sealed class PeerAddressStartupFilter(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
            => app =>
            {
                app.Use((context, continuation) =>
                {
                    context.Connection.RemoteIpAddress = address;
                    context.Connection.RemotePort = 43210;
                    return continuation(context);
                });
                next(app);
            };
    }

    private sealed class StubClientFactory(
        ConcurrentQueue<ForwardedRequest> requests,
        Func<ForwardedRequest, CancellationToken, Task<HttpResponseMessage>>? backend)
        : IForwarderHttpClientFactory
    {
        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context)
            => new(new StubHandler(context.ClusterId, requests, backend));
    }

    private sealed class StubHandler(
        string cluster,
        ConcurrentQueue<ForwardedRequest> requests,
        Func<ForwardedRequest, CancellationToken, Task<HttpResponseMessage>>? backend)
        : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var captured = new ForwardedRequest(
                cluster,
                request.Method.Method,
                request.RequestUri!.AbsolutePath,
                request.RequestUri.Query,
                body,
                request.Headers.Authorization?.ToString(),
                request.Headers.Host ?? request.RequestUri.Authority,
                Header("X-Forwarded-For"),
                Header("X-Forwarded-Proto"),
                Header("X-Forwarded-Host"));
            requests.Enqueue(captured);

            return backend is null
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = request,
                    Content = JsonContent.Create(captured)
                }
                : await backend(captured, cancellationToken);

            string? Header(string name) => request.Headers.TryGetValues(name, out var values)
                ? string.Join(", ", values)
                : null;
        }
    }
}

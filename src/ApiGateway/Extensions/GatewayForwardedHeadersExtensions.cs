using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using IPNetwork = Microsoft.AspNetCore.HttpOverrides.IPNetwork;

namespace ApiGateway.Extensions;

public static class GatewayForwardedHeadersExtensions
{
    public static IServiceCollection AddGatewayForwardedHeaders(
        this IServiceCollection services,
        bool isAzureAppService)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor |
                ForwardedHeaders.XForwardedProto;

            options.ForwardLimit = 1;

            if (!isAzureAppService)
            {
                return;
            }

            var networks = new[]
            {
                ("10.0.0.0", 8),
                ("172.16.0.0", 12),
                ("192.168.0.0", 16)
            };

            foreach (var (address, prefixLength) in networks)
            {
                var networkAddress = IPAddress.Parse(address);

                options.KnownNetworks.Add(
                    new IPNetwork(networkAddress, prefixLength));

                options.KnownNetworks.Add(
                    new IPNetwork(
                        networkAddress.MapToIPv6(),
                        prefixLength + 96));
            }
        });

        return services;
    }
}

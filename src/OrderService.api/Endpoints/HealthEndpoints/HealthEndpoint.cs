using Microsoft.Extensions.Diagnostics.HealthChecks;
using MinimalApi.Endpoint;

namespace OrderService.api.Endpoints.HealthEndpoints;

/// <summary>
/// Viser servicens helbred i Swagger. Kører de registrerede health checks (databasen) og svarer
/// 200 hvis alt er sundt, ellers 503.
/// </summary>
public class HealthEndpoint : IEndpoint<IResult, HealthCheckService>
{
    public async Task<IResult> HandleAsync(HealthCheckService healthCheckService)
    {
        var report = await healthCheckService.CheckHealthAsync();

        var response = new
        {
            Status = report.Status.ToString(),
            Checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString())
        };

        return report.Status == HealthStatus.Healthy
            ? Results.Ok(response)
            : Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("health", async (HealthCheckService healthCheckService) =>
                await HandleAsync(healthCheckService))
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status503ServiceUnavailable)
            .WithTags("HealthEndpoints");
    }
}

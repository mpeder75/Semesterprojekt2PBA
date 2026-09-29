using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.CatalogItemEndpoints;

/// <summary>
/// Deletes a Catalog Item
/// </summary>
public class DeleteCatalogItemEndpoint : IEndpoint<IResult, DeleteCatalogItemRequest, ICatalogClient>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/catalog-items/{catalogItemId}",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int catalogItemId, ICatalogClient catalogClient) =>
            {
                return await HandleAsync(new DeleteCatalogItemRequest(catalogItemId), catalogClient);
            })
            .Produces<DeleteCatalogItemResponse>()
            .WithTags("CatalogItemEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteCatalogItemRequest request, ICatalogClient catalogClient)
    {
        var response = new DeleteCatalogItemResponse(request.CorrelationId());

        if (!await catalogClient.DeleteItemAsync(request.CatalogItemId)) return Results.NotFound();

        return Results.Ok(response);
    }
}

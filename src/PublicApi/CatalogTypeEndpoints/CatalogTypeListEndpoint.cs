using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.CatalogTypeEndpoints;

/// <summary>
/// List Catalog Types
/// </summary>
public class CatalogTypeListEndpoint : IEndpoint<IResult, ICatalogClient>
{
    private readonly IMapper _mapper;

    public CatalogTypeListEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/catalog-types",
            async (ICatalogClient catalogClient) =>
            {
                return await HandleAsync(catalogClient);
            })
            .Produces<ListCatalogTypesResponse>()
            .WithTags("CatalogTypeEndpoints");
    }

    public async Task<IResult> HandleAsync(ICatalogClient catalogClient)
    {
        var response = new ListCatalogTypesResponse();

        var items = await catalogClient.GetTypesAsync();

        response.CatalogTypes.AddRange(items.Select(_mapper.Map<CatalogTypeDto>));

        return Results.Ok(response);
    }
}

using BasketService.api.Interfaces;
using MinimalApi.Endpoint;

namespace BasketService.api.Endpoints.BasketEndpoints
{
    public sealed class AddItemToBasketEndpoint
        : IEndpoint<AddItemToBasketEndpoint, IResult>
    {
        public void AddRoute(IEndpointRouteBuilder app)
        {
            
            
        }

        public Task<AddItemToBasketEndpoint> HandleAsync(IResult request)
        {
            throw new NotImplementedException();
        }
    }
}

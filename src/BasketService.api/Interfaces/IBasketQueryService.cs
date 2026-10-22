namespace BasketService.api.Interfaces
{
    public interface IBasketQueryService 
    {
        Task<int> CountTotalBasketItems(string username);
    }
}

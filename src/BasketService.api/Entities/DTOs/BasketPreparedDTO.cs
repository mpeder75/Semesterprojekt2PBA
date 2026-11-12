namespace BasketService.api.Entities.DTOs;

public class BasketPreparedDTO
{
   
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }

    public BasketPreparedDTO(int catalogItemId, int quantity)
    {
        CatalogItemId = catalogItemId;
        Quantity = quantity;
    }

}

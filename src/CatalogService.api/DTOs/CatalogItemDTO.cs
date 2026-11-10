namespace CatalogService.api.DTOs;

public class CatalogItemDTO
{
    

    public int CatalogItemId { get; set; }
    public string Name { get; set; }
    public decimal price { get; set; }
    public string PictureUri { get; set; }

    public CatalogItemDTO(int catalogItemId, string name, decimal price, string pictureUri)
    {
        CatalogItemId = catalogItemId;
        Name = name;
        this.price = price;
        PictureUri = pictureUri;
    }
}

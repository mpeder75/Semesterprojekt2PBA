namespace OrderService.api.Messages.Catalog;

public class CatalogItemDto
{
    public int CatalogItemId { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
    public string PictureUri { get; set; }
}

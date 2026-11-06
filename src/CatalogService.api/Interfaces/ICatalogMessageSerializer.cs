namespace CatalogService.api.Interfaces;

public interface ICatalogMessageSerializer
{
    string Serialize (ICatalogMessage message);
}

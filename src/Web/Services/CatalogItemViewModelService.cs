using Ardalis.GuardClauses;
using Microsoft.eShopWeb.Catalog.Contracts;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Web.Interfaces;
using Microsoft.eShopWeb.Web.ViewModels;

namespace Microsoft.eShopWeb.Web.Services;

public class CatalogItemViewModelService : ICatalogItemViewModelService
{
    private readonly ICatalogClient _catalogClient;

    public CatalogItemViewModelService(ICatalogClient catalogClient)
    {
        _catalogClient = catalogClient;
    }

    public async Task UpdateCatalogItem(CatalogItemViewModel viewModel)
    {
        var existingCatalogItem = await _catalogClient.GetItemByIdAsync(viewModel.Id);

        Guard.Against.Null(existingCatalogItem, nameof(existingCatalogItem));

        var updated = await _catalogClient.UpdateItemAsync(viewModel.Id, new CatalogItemWrite(
            viewModel.Name, existingCatalogItem.Description, viewModel.Price,
            existingCatalogItem.CatalogBrandId, existingCatalogItem.CatalogTypeId));
        Guard.Against.Null(updated, nameof(updated));
    }
}

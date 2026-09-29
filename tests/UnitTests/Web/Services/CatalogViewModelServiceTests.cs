using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Catalog.Contracts;
using Microsoft.eShopWeb.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Web.Services;

public class CatalogViewModelServiceTests
{
    [Fact]
    public async Task BuildsFilteredPageAndDropdownsFromCatalogClient()
    {
        var client = Substitute.For<ICatalogClient>();
        client.GetItemsAsync(1, 2, 3, 4).Returns(new CatalogPage(
            new[] { new CatalogItemDto(7, "Shirt", "Cotton", 15m, "shirt.png", 3, 4) }, 3));
        client.GetBrandsAsync().Returns(new[] { new CatalogBrandDto(3, "Zeta"), new CatalogBrandDto(5, "Alpha") });
        client.GetTypesAsync().Returns(new[] { new CatalogTypeDto(4, "Shirts") });
        var composer = Substitute.For<IUriComposer>();
        composer.ComposePicUri("shirt.png").Returns("https://images.example/shirt.png");
        var service = new CatalogViewModelService(NullLoggerFactory.Instance, client, composer);

        var page = await service.GetCatalogItems(1, 2, 3, 4);

        Assert.NotNull(page.PaginationInfo);
        Assert.NotNull(page.Brands);
        Assert.NotNull(page.Types);
        var item = Assert.Single(page.CatalogItems);
        Assert.Equal(7, item.Id);
        Assert.Equal("Shirt", item.Name);
        Assert.Equal(15m, item.Price);
        Assert.Equal("https://images.example/shirt.png", item.PictureUri);
        Assert.Equal(3, page.PaginationInfo.TotalItems);
        Assert.Equal(2, page.PaginationInfo.TotalPages);
        Assert.Equal(1, page.PaginationInfo.ItemsPerPage);
        Assert.Equal(1, page.PaginationInfo.ActualPage);
        Assert.Equal("is-disabled", page.PaginationInfo.Next);
        Assert.Equal("", page.PaginationInfo.Previous);
        Assert.Equal(3, page.BrandFilterApplied);
        Assert.Equal(4, page.TypesFilterApplied);
        Assert.Equal(new[] { "All", "Alpha", "Zeta" }, page.Brands.Select(b => b.Text));
        Assert.Equal(new[] { "All", "Shirts" }, page.Types.Select(t => t.Text));
        Assert.True(page.Brands.First().Selected);
    }
}

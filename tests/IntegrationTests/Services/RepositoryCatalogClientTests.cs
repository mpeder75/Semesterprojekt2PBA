using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Services;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Services;

public class RepositoryCatalogClientTests : IDisposable
{
    private readonly CatalogContext _context;
    private readonly RepositoryCatalogClient _client;

    public RepositoryCatalogClientTests()
    {
        _context = new CatalogContext(new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _client = new RepositoryCatalogClient(new EfRepository<CatalogItem>(_context),
            new EfRepository<CatalogBrand>(_context), new EfRepository<CatalogType>(_context));
    }

    [Fact]
    public async Task FiltersBeforePagingAndCountsAllMatches()
    {
        _context.CatalogItems.AddRange(
            new CatalogItem(1, 1, "First", "A", 10m, "a.png"),
            new CatalogItem(1, 1, "Second", "B", 20m, "b.png"),
            new CatalogItem(2, 1, "Other type", "C", 30m, "c.png"),
            new CatalogItem(1, 2, "Other brand", "D", 40m, "d.png"));
        await _context.SaveChangesAsync();

        var first = await _client.GetItemsAsync(0, 1, 1, 1);
        var second = await _client.GetItemsAsync(1, 1, 1, 1);
        var beyond = await _client.GetItemsAsync(2, 1, 1, 1);
        var all = await _client.GetItemsAsync(0, 10, null, null);

        Assert.Equal(2, first.TotalItems);
        Assert.Equal(2, second.TotalItems);
        Assert.NotEqual(Assert.Single(first.Items).Id, Assert.Single(second.Items).Id);
        Assert.All(first.Items.Concat(second.Items), item =>
        {
            Assert.Equal(1, item.CatalogBrandId);
            Assert.Equal(1, item.CatalogTypeId);
        });
        Assert.Empty(beyond.Items);
        Assert.Equal(2, beyond.TotalItems);
        Assert.Equal(4, all.TotalItems);
        Assert.Equal(4, all.Items.Count);
    }

    [Fact]
    public async Task LooksUpItemsAndHandlesMissingOrEmptyIds()
    {
        var item = new CatalogItem(2, 3, "Cotton", "Shirt", 15m, "shirt.png");
        _context.CatalogItems.Add(item);
        await _context.SaveChangesAsync();

        var dto = await _client.GetItemByIdAsync(item.Id);

        Assert.NotNull(dto);
        Assert.Equal(item.Id, dto.Id);
        Assert.Equal("Shirt", dto.Name);
        Assert.Equal("Cotton", dto.Description);
        Assert.Equal(15m, dto.Price);
        Assert.Equal("shirt.png", dto.PictureUri);
        Assert.Equal(3, dto.CatalogBrandId);
        Assert.Equal(2, dto.CatalogTypeId);
        Assert.Null(await _client.GetItemByIdAsync(int.MaxValue));
        var batch = await _client.GetItemsByIdsAsync(new[] { item.Id, item.Id, int.MaxValue });
        Assert.Equal(dto, Assert.Single(batch));
        Assert.Empty(await _client.GetItemsByIdsAsync(Array.Empty<int>()));
    }

    [Fact]
    public async Task ReturnsBrandAndTypeData()
    {
        var brand = new CatalogBrand("Acme");
        var type = new CatalogType("Shirts");
        _context.CatalogBrands.Add(brand);
        _context.CatalogTypes.Add(type);
        await _context.SaveChangesAsync();

        var brandDto = Assert.Single(await _client.GetBrandsAsync());
        var typeDto = Assert.Single(await _client.GetTypesAsync());

        Assert.Equal(brand.Id, brandDto.Id);
        Assert.Equal("Acme", brandDto.Brand);
        Assert.Equal(type.Id, typeDto.Id);
        Assert.Equal("Shirts", typeDto.Type);
    }

    public void Dispose() => _context.Dispose();
}

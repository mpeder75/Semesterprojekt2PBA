using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Catalog.Api.Data;
using Microsoft.eShopWeb.Catalog.Contracts;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CatalogServiceTests;

public class CatalogApiTests
{
    [Fact]
    public async Task RequiresServiceCredentialForReadsAndWrites()
    {
        using var factory = new CatalogFactory();
        using var http = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/catalog/items")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync("/catalog/items", new CatalogItemWrite("Test", "Test", 10, 1, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task HttpClientPreservesPagingFilteringAndLookups()
    {
        using var factory = new CatalogFactory();
        using var http = factory.AuthorizedClient();
        var client = new HttpCatalogClient(http);
        var page = await client.GetItemsAsync(1, 2, 2, 2);
        Assert.Equal(3, page.TotalItems);
        Assert.Equal(6, Assert.Single(page.Items).Id);
        Assert.Equal(5, (await client.GetBrandsAsync()).Count);
        Assert.Equal(4, (await client.GetTypesAsync()).Count);
        Assert.Null(await client.GetItemByIdAsync(9999));
        Assert.Empty(await client.GetItemsByIdsAsync([]));
        Assert.Equal(new[] { 1, 3 }, (await client.GetItemsByIdsAsync([3, 1, 1, 9999])).Select(i => i.Id));
        Assert.Equal(12, (await client.GetItemsAsync(0, 0, null, null)).Items.Count);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync("/catalog/items?pageIndex=-1")).StatusCode);
    }

    [Fact]
    public async Task HttpClientCanCreateUpdateAndDeleteWithoutMonolithDatabase()
    {
        using var factory = new CatalogFactory();
        using var http = factory.AuthorizedClient();
        var client = new HttpCatalogClient(http);
        var request = new CatalogItemWrite("New product", "Description", 20m, 1, 2);
        var created = await client.CreateItemAsync(request);
        Assert.True(created.Id > 12);
        Assert.Equal("/images/products/eCatalog-item-default.png", created.PictureUri);
        await Assert.ThrowsAsync<Microsoft.eShopWeb.ApplicationCore.Exceptions.DuplicateException>(() => client.CreateItemAsync(request));
        var updated = await client.UpdateItemAsync(created.Id, request with { Price = 25m });
        Assert.Equal(25m, updated!.Price);
        Assert.Equal(25m, (await client.GetItemByIdAsync(created.Id))!.Price);
        Assert.True(await client.DeleteItemAsync(created.Id));
        Assert.False(await client.DeleteItemAsync(created.Id));
        Assert.Null(await client.UpdateItemAsync(created.Id, request));
        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateItemAsync(request with { CatalogBrandId = 9999 }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateItemAsync(request with { Price = -1 }));
    }

    [Fact]
    public async Task CreateReplaysSavedResultAndRejectsChangedPayload()
    {
        using var factory = new CatalogFactory();
        using var http = factory.AuthorizedClient();
        http.DefaultRequestHeaders.Add("Idempotency-Key", "same-operation");
        var body = new CatalogItemWrite("Retry test", "Description", 20m, 1, 2);
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => http.PostAsJsonAsync("/catalog/items", body)));
        var created = new List<CatalogItemDto>();
        foreach (var response in responses)
        {
            using (response)
            {
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                created.Add((await response.Content.ReadFromJsonAsync<CatalogItemDto>())!);
            }
        }
        Assert.Single(created.Select(i => i.Id).Distinct());
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await http.PostAsJsonAsync("/catalog/items", body with { Price = 30 })).StatusCode);
        await http.DeleteAsync($"/catalog/items/{created[0].Id}");
        // The same operation returns its original response even after the product is deleted.
        Assert.Equal(created[0], await (await http.PostAsJsonAsync("/catalog/items", body)).Content.ReadFromJsonAsync<CatalogItemDto>());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Assert.Equal(12, await db.Items.CountAsync());
        Assert.Single(await db.Requests.ToListAsync());
    }

    [Fact]
    public async Task DuplicateNameIsRejectedOnUpdateWithoutChangingProduct()
    {
        using var factory = new CatalogFactory();
        using var http = factory.AuthorizedClient();
        var before = await http.GetFromJsonAsync<CatalogItemDto>("/catalog/items/1");
        var other = await http.GetFromJsonAsync<CatalogItemDto>("/catalog/items/2");
        var update = new CatalogItemWrite(other!.Name, "Changed", 100, 1, 1);
        Assert.Equal(HttpStatusCode.Conflict, (await http.PutAsJsonAsync("/catalog/items/1", update)).StatusCode);
        Assert.Equal(before, await http.GetFromJsonAsync<CatalogItemDto>("/catalog/items/1"));
    }

    [Fact]
    public async Task ClientRetriesLostCreateResponseWithTheSameKey()
    {
        using var factory = new CatalogFactory();
        using var handler = new LoseFirstResponseHandler(factory.Server.CreateHandler());
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        http.DefaultRequestHeaders.Add("X-Catalog-Key", CatalogFactory.TestKey);
        var client = new HttpCatalogClient(http);
        var created = await client.CreateItemAsync(new CatalogItemWrite("Lost response", "Test", 10, 1, 1));
        Assert.Equal(2, handler.Keys.Count);
        Assert.False(string.IsNullOrEmpty(handler.Keys[0]));
        Assert.Equal(handler.Keys[0], handler.Keys[1]);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Assert.Single(await db.Items.Where(i => i.Name == created.Name).ToListAsync());
    }

    private sealed class LoseFirstResponseHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        public List<string> Keys { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Keys.Add(request.Headers.GetValues("Idempotency-Key").Single());
            var response = await base.SendAsync(request, token);
            if (Keys.Count == 1)
            {
                response.Dispose();
                throw new HttpRequestException("Simulated lost response after commit.");
            }
            return response;
        }
    }

    [Fact]
    public async Task SnapshotPreservesIdsAndRefusesOverwrite()
    {
        using var db = new CatalogDbContext(new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var snapshot = new CatalogSnapshot([new(8, "Brand")], [new(9, "Type")],
            [new(123, "Imported", "Existing product", 12, "image.png", 8, 9)]);
        await CatalogTransfer.ImportAsync(db, snapshot);
        var exported = await CatalogTransfer.SnapshotAsync(db);
        Assert.Equal(snapshot.Items, exported.Items);
        Assert.Equal(snapshot.Brands, exported.Brands);
        Assert.Equal(snapshot.Types, exported.Types);
        await Assert.ThrowsAsync<InvalidOperationException>(() => CatalogTransfer.ImportAsync(db, snapshot));
    }
}

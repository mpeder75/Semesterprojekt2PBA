using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.eShopWeb.Web.Services;
using Microsoft.eShopWeb.Web.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Web.Services;

public class BasketApiClientTests
{
    private const string Buyer = "kunde@test.dk";

    private readonly FakeBasketService _basketService = new();

    private BasketApiClient Client() =>
        new(new HttpClient(_basketService) { BaseAddress = new Uri("http://basket.test/") }, NullLogger<BasketApiClient>.Instance);

    [Fact]
    public async Task NewBasket_ContainsExactlyTheLines_AndReturnsTheBasketId()
    {
        var id = await Client().ReplaceBasketAsync(Buyer, new[]
        {
            new BasketLine(1, 19.5m, 2),
            new BasketLine(2, 8.5m, 1)
        });

        Assert.Equal(FakeBasketService.BasketId, id);
        Assert.Equal(new[] { (1, 2), (2, 1) }, _basketService.Quantities());
    }

    [Fact]
    public async Task OldBasketFromAnEarlierAttempt_IsCorrectedToTheWantedLines()
    {
        // Et tidligere mislykket forsøg lod vare 1 (antal 5) og vare 9 (antal 3) ligge i BasketService
        _basketService.Seed(catalogItemId: 1, quantity: 5);
        _basketService.Seed(catalogItemId: 9, quantity: 3);

        await Client().ReplaceBasketAsync(Buyer, new[]
        {
            new BasketLine(1, 19.5m, 2),
            new BasketLine(2, 8.5m, 1)
        });

        // Vare 1 er rettet til 2, vare 2 er kommet til, og vare 9 er fjernet
        Assert.Equal(new[] { (1, 2), (2, 1) }, _basketService.Quantities());
    }

    [Fact]
    public async Task RepeatingTheSameCall_DoesNotDoubleTheQuantities()
    {
        var lines = new[] { new BasketLine(1, 19.5m, 2), new BasketLine(2, 8.5m, 1) };

        var first = await Client().ReplaceBasketAsync(Buyer, lines);
        var second = await Client().ReplaceBasketAsync(Buyer, lines);

        Assert.Equal(first, second);
        Assert.Equal(new[] { (1, 2), (2, 1) }, _basketService.Quantities());
    }

    [Fact]
    public async Task SeveralLinesForTheSameItem_AreSummed()
    {
        await Client().ReplaceBasketAsync(Buyer, new[]
        {
            new BasketLine(1, 19.5m, 2),
            new BasketLine(1, 19.5m, 3)
        });

        Assert.Equal(new[] { (1, 5) }, _basketService.Quantities());
    }

    [Fact]
    public async Task BuyerIdWithAtSign_IsEscapedInTheAddress()
    {
        await Client().ReplaceBasketAsync(Buyer, new[] { new BasketLine(1, 19.5m, 1) });

        Assert.Contains(_basketService.RequestPaths, p => p.StartsWith("/basket/kunde%40test.dk/items") || p.StartsWith("/basket/kunde@test.dk/items"));
    }

    [Fact]
    public async Task QuantitiesAreSetThroughTheBasketsOwnEndpoint()
    {
        await Client().ReplaceBasketAsync(Buyer, new[] { new BasketLine(1, 19.5m, 1) });

        Assert.Contains($"/basket/{FakeBasketService.BasketId}/items/quantities", _basketService.RequestPaths);
    }

    [Fact]
    public async Task WhenBasketServiceAnswersWithAnError_ThrowsBasketBridgeException()
    {
        _basketService.FailAddWith = HttpStatusCode.InternalServerError;

        await Assert.ThrowsAsync<BasketBridgeException>(() =>
            Client().ReplaceBasketAsync(Buyer, new[] { new BasketLine(1, 19.5m, 1) }));
    }

    [Fact]
    public async Task WhenBasketServiceCannotBeReached_ThrowsBasketBridgeException()
    {
        _basketService.Throw = new HttpRequestException("connection refused");

        await Assert.ThrowsAsync<BasketBridgeException>(() =>
            Client().ReplaceBasketAsync(Buyer, new[] { new BasketLine(1, 19.5m, 1) }));
    }

    [Fact]
    public async Task NoLines_IsRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Client().ReplaceBasketAsync(Buyer, Array.Empty<BasketLine>()));
    }

    /// <summary>
    /// En lille falsk BasketService, der opfører sig som den rigtige: at lægge en vare til lægger antallet til det,
    /// der er, antal rettes ud fra linjens eget id, og linjer med antal nul fjernes.
    /// </summary>
    private sealed class FakeBasketService : HttpMessageHandler
    {
        public const int BasketId = 7;

        private int _nextRowId = 100;
        private readonly List<Row> _rows = new();

        public HttpStatusCode? FailAddWith { get; set; }
        public Exception? Throw { get; set; }
        public List<string> RequestPaths { get; } = new();

        public void Seed(int catalogItemId, int quantity) => _rows.Add(new Row(_nextRowId++, catalogItemId, quantity));

        public (int CatalogItemId, int Quantity)[] Quantities() =>
            _rows.OrderBy(r => r.CatalogItemId).Select(r => (r.CatalogItemId, r.Quantity)).ToArray();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Throw is not null)
            {
                throw Throw;
            }

            RequestPaths.Add(request.RequestUri!.AbsolutePath);
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var json = JsonDocument.Parse(body);

            if (request.Method == HttpMethod.Post)
            {
                if (FailAddWith is { } status)
                {
                    return new HttpResponseMessage(status);
                }

                var catalogItemId = json.RootElement.GetProperty("catalogItemId").GetInt32();
                var quantity = json.RootElement.GetProperty("quantity").GetInt32();
                var existing = _rows.FirstOrDefault(r => r.CatalogItemId == catalogItemId);

                if (existing is null)
                {
                    _rows.Add(new Row(_nextRowId++, catalogItemId, quantity));
                }
                else
                {
                    existing.Quantity += quantity;
                }

                return Basket();
            }

            if (request.Method == HttpMethod.Put)
            {
                foreach (var entry in json.RootElement.GetProperty("quantities").EnumerateObject())
                {
                    var row = _rows.FirstOrDefault(r => r.RowId.ToString() == entry.Name);

                    if (row is not null)
                    {
                        row.Quantity = entry.Value.GetInt32();
                    }
                }

                _rows.RemoveAll(r => r.Quantity == 0);
                return Basket();
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private HttpResponseMessage Basket()
        {
            var items = _rows.Select(r => new { id = r.RowId, catalogItemId = r.CatalogItemId, quantity = r.Quantity });
            var content = JsonSerializer.Serialize(new { id = BasketId, buyerId = Buyer, items });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "application/json") };
        }

        private sealed class Row
        {
            public Row(int rowId, int catalogItemId, int quantity)
            {
                RowId = rowId;
                CatalogItemId = catalogItemId;
                Quantity = quantity;
            }

            public int RowId { get; }
            public int CatalogItemId { get; }
            public int Quantity { get; set; }
        }
    }
}

using System.Net;
using System.Text;
using Microsoft.eShopWeb.Web.Services;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Web.Services;

public class OrderApiClientTests
{
    private const string Buyer = "kunde@test.dk";

    private const string ListJson = """
        {"orders":[
          {"id":24,"orderDate":"2026-11-14T09:00:00+01:00","total":47.5,"status":"Confirmed",
           "address":{"street":"Vej 1","city":"By","state":"DK","country":"Danmark","zipCode":"1000"}},
          {"id":26,"orderDate":"2026-11-14T12:00:00+01:00","total":52,"status":"PendingInventory",
           "address":{"street":"Vej 1","city":"By","state":"DK","country":"Danmark","zipCode":"1000"}},
          {"id":25,"orderDate":"2026-11-14T10:00:00+01:00","total":10,"status":"Rejected",
           "address":{"street":"Vej 1","city":"By","state":"DK","country":"Danmark","zipCode":"1000"}}
        ]}
        """;

    private const string DetailJson = """
        {"order":{"id":24,"orderDate":"2026-11-14T09:00:00+01:00","total":47.5,"status":"Confirmed",
          "address":{"street":"Vej 1","city":"By","state":"DK","country":"Danmark","zipCode":"1000"},
          "orderItems":[
            {"productId":1,"productName":"Sweatshirt","pictureUrl":"/images/products/1.png","unitPrice":19.5,"units":2},
            {"productId":2,"productName":"Mug","pictureUrl":"/images/products/2.png","unitPrice":8.5,"units":1}
          ]}}
        """;

    private readonly StubOrderService _orderService = new();

    private OrderApiClient Client() =>
        new(new HttpClient(_orderService) { BaseAddress = new Uri("http://order.test/") });

    [Fact]
    public async Task Orders_AreListedNewestFirst_WithTheirOwnAddress()
    {
        var orders = await Client().GetOrdersAsync(Buyer);

        Assert.Equal(new[] { 26, 25, 24 }, orders.Select(o => o.OrderNumber));
        Assert.Equal("Vej 1", orders[0].ShippingAddress!.Street);
        Assert.Equal(52m, orders[0].Total);
    }

    [Fact]
    public async Task StatusIsShownTheWayACustomerUnderstandsIt()
    {
        var orders = await Client().GetOrdersAsync(Buyer);

        Assert.Equal("Processing", orders.Single(o => o.OrderNumber == 26).Status);
        Assert.Equal("Rejected", orders.Single(o => o.OrderNumber == 25).Status);
        Assert.Equal("Confirmed", orders.Single(o => o.OrderNumber == 24).Status);
    }

    [Fact]
    public async Task BuyerIdIsSentUrlEncodedToOrderService()
    {
        await Client().GetOrdersAsync(Buyer);

        Assert.Contains("buyerId=kunde%40test.dk", _orderService.LastListQuery);
    }

    [Fact]
    public async Task OwnOrder_IsReturnedWithItemsAndAddress()
    {
        var order = await Client().GetOrderAsync(Buyer, 24);

        Assert.NotNull(order);
        Assert.Equal(24, order!.OrderNumber);
        Assert.Equal("Confirmed", order.Status);
        Assert.Equal(47.5m, order.Total);
        Assert.Equal("Danmark", order.ShippingAddress!.Country);
        Assert.Equal(new[] { "Sweatshirt", "Mug" }, order.OrderItems.Select(i => i.ProductName));
        Assert.Equal(2, order.OrderItems[0].Units);
    }

    [Fact]
    public async Task AnotherBuyersOrder_IsNotReturned_AndTheDetailIsNeverFetched()
    {
        // Ordre 99 findes hos OrderService, men er ikke på denne købers liste
        var order = await Client().GetOrderAsync(Buyer, 99);

        Assert.Null(order);
        Assert.False(_orderService.DetailWasFetched);
    }

    [Fact]
    public async Task WhenOrderServiceIsDown_ThrowsHttpRequestException()
    {
        _orderService.ListStatus = HttpStatusCode.ServiceUnavailable;

        await Assert.ThrowsAsync<HttpRequestException>(() => Client().GetOrdersAsync(Buyer));
    }

    private sealed class StubOrderService : HttpMessageHandler
    {
        public HttpStatusCode ListStatus { get; set; } = HttpStatusCode.OK;
        public string? LastListQuery { get; private set; }
        public bool DetailWasFetched { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path == "/api/orders")
            {
                LastListQuery = request.RequestUri.Query;
                return Task.FromResult(Reply(ListStatus, ListStatus == HttpStatusCode.OK ? ListJson : ""));
            }

            if (path.StartsWith("/api/orders/"))
            {
                DetailWasFetched = true;
                return Task.FromResult(Reply(HttpStatusCode.OK, DetailJson));
            }

            return Task.FromResult(Reply(HttpStatusCode.NotFound, ""));
        }

        private static HttpResponseMessage Reply(HttpStatusCode status, string json) =>
            new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}

using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.eShopWeb.Web.Services;
using Microsoft.eShopWeb.Web.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Web.Services;

public class CheckoutApiClientTests
{
    private static readonly CheckoutAddress Address = new("Vej 1", "By", "DK", "Danmark", "1000");

    private readonly StubHandler _handler = new();

    private CheckoutApiClient Client() =>
        new(new HttpClient(_handler) { BaseAddress = new Uri("http://order.test/") }, NullLogger<CheckoutApiClient>.Instance);

    [Fact]
    public async Task Start_SendsPostWithIdempotencyKeyAndJsonBody()
    {
        var key = Guid.NewGuid();
        _handler.Respond(HttpStatusCode.Accepted);

        var result = await Client().StartAsync(key, 5, "buyer@test.dk", Address);

        Assert.Equal(StartCheckoutOutcome.Accepted, result.Outcome);
        Assert.Equal(key, result.CheckoutId);

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://order.test/api/orders/checkouts", request.Uri);
        Assert.Equal(key.ToString(), request.IdempotencyKey);

        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal(5, body.RootElement.GetProperty("basketId").GetInt32());
        Assert.Equal("buyer@test.dk", body.RootElement.GetProperty("buyerId").GetString());
        Assert.Equal("Vej 1", body.RootElement.GetProperty("shipToAddress").GetProperty("street").GetString());
        Assert.Equal("1000", body.RootElement.GetProperty("shipToAddress").GetProperty("zipCode").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, StartCheckoutOutcome.Conflict)]
    [InlineData(HttpStatusCode.BadRequest, StartCheckoutOutcome.Rejected)]
    [InlineData(HttpStatusCode.InternalServerError, StartCheckoutOutcome.Unavailable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, StartCheckoutOutcome.Unavailable)]
    public async Task Start_MapsErrorStatusCodesToOutcomes(HttpStatusCode status, StartCheckoutOutcome expected)
    {
        _handler.Respond(status, "problem");

        var result = await Client().StartAsync(Guid.NewGuid(), 5, "buyer@test.dk", Address);

        Assert.Equal(expected, result.Outcome);
        Assert.Null(result.CheckoutId);
    }

    [Fact]
    public async Task Start_WhenOrderServiceCannotBeReached_ReturnsUnavailable()
    {
        _handler.Throw(new HttpRequestException("connection refused"));

        var result = await Client().StartAsync(Guid.NewGuid(), 5, "buyer@test.dk", Address);

        Assert.Equal(StartCheckoutOutcome.Unavailable, result.Outcome);
    }

    [Fact]
    public async Task GetStatus_ReadsStatusAndOrderId()
    {
        var id = Guid.NewGuid();
        _handler.Respond(HttpStatusCode.OK,
            $$"""{"checkoutId":"{{id}}","state":"Completed","status":"Completed","failureReason":null,"orderId":17}""");

        var status = await Client().GetStatusAsync(id);

        Assert.NotNull(status);
        Assert.Equal("Completed", status!.Status);
        Assert.Equal(17, status.OrderId);
        Assert.True(status.IsFinished);
        Assert.Equal($"http://order.test/api/orders/checkouts/{id}", Assert.Single(_handler.Requests).Uri);
    }

    [Fact]
    public async Task GetStatus_ForProcessingCheckout_IsNotFinished()
    {
        _handler.Respond(HttpStatusCode.OK, """{"status":"Processing","state":"AwaitingCatalog","orderId":null}""");

        var status = await Client().GetStatusAsync(Guid.NewGuid());

        Assert.False(status!.IsFinished);
        Assert.Null(status.OrderId);
    }

    [Fact]
    public async Task GetStatus_ForUnknownCheckout_ReturnsNull()
    {
        _handler.Respond(HttpStatusCode.NotFound);

        Assert.Null(await Client().GetStatusAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetStatus_OnServerError_Throws()
    {
        _handler.Respond(HttpStatusCode.ServiceUnavailable);

        await Assert.ThrowsAsync<HttpRequestException>(() => Client().GetStatusAsync(Guid.NewGuid()));
    }

    private sealed record Captured(HttpMethod Method, string Uri, string? IdempotencyKey, string? Body);

    private sealed class StubHandler : HttpMessageHandler
    {
        private HttpStatusCode _status = HttpStatusCode.OK;
        private string _content = string.Empty;
        private Exception? _error;

        public List<Captured> Requests { get; } = new();

        public void Respond(HttpStatusCode status, string content = "")
        {
            _status = status;
            _content = content;
        }

        public void Throw(Exception error) => _error = error;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var key = request.Headers.TryGetValues("Idempotency-Key", out var values) ? values.Single() : null;
            Requests.Add(new Captured(request.Method, request.RequestUri!.ToString(), key, body));

            if (_error is not null)
            {
                throw _error;
            }

            return new HttpResponseMessage(_status) { Content = new StringContent(_content, Encoding.UTF8, "application/json") };
        }
    }
}

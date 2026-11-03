using ApiGateway.Resilience;
using Yarp.ReverseProxy.Forwarder;

namespace ApiGateway.Tests;

public sealed class ProxyOutcomeClassifierTests
{
    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(204)]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(429)]
    [InlineData(501)]
    public void Responses_outside_the_server_failure_policy_do_not_count(int statusCode)
    {
        var outcome = ProxyOutcomeClassifier.Classify(
            statusCode, ForwarderError.None, clientAborted: false);

        Assert.False(outcome.IsBackendFailure);
    }

    [Theory]
    [InlineData(500, ForwarderError.None)]
    [InlineData(502, ForwarderError.None)]
    [InlineData(503, ForwarderError.None)]
    [InlineData(504, ForwarderError.None)]
    [InlineData(502, ForwarderError.Request)]
    [InlineData(504, ForwarderError.RequestTimedOut)]
    [InlineData(502, ForwarderError.RequestBodyDestination)]
    [InlineData(200, ForwarderError.ResponseBodyDestination)]
    [InlineData(504, ForwarderError.RequestCanceled)]
    public void Backend_http_and_transport_failures_count(
        int statusCode, ForwarderError error)
    {
        var outcome = ProxyOutcomeClassifier.Classify(
            statusCode, error, clientAborted: false);

        Assert.True(outcome.IsBackendFailure);
    }

    [Theory]
    [InlineData(502, ForwarderError.RequestBodyClient)]
    [InlineData(502, ForwarderError.ResponseBodyClient)]
    [InlineData(502, ForwarderError.ResponseHeaders)]
    [InlineData(500, ForwarderError.RequestCreation)]
    [InlineData(503, ForwarderError.NoAvailableDestinations)]
    [InlineData(504, ForwarderError.RequestBodyCanceled)]
    [InlineData(504, ForwarderError.ResponseBodyCanceled)]
    [InlineData(400, ForwarderError.RequestCanceled)]
    public void Client_ambiguous_and_local_proxy_errors_do_not_count(
        int statusCode, ForwarderError error)
    {
        var outcome = ProxyOutcomeClassifier.Classify(
            statusCode, error, clientAborted: false);

        Assert.False(outcome.IsBackendFailure);
    }

    [Theory]
    [InlineData(503, ForwarderError.None)]
    [InlineData(502, ForwarderError.Request)]
    [InlineData(504, ForwarderError.RequestTimedOut)]
    [InlineData(504, ForwarderError.RequestCanceled)]
    public void Actual_client_cancellation_is_not_counted_as_backend_failure(
        int statusCode, ForwarderError error)
    {
        var outcome = ProxyOutcomeClassifier.Classify(
            statusCode, error, clientAborted: true);

        Assert.False(outcome.IsBackendFailure);
        Assert.Equal("client_canceled", outcome.Reason);
    }

    [Fact]
    public void An_unknown_forwarder_error_does_not_default_to_blame_the_backend()
    {
        var outcome = ProxyOutcomeClassifier.Classify(
            502, (ForwarderError)9999, clientAborted: false);

        Assert.False(outcome.IsBackendFailure);
    }

    [Theory]
    [InlineData(ForwarderError.RequestBodyDestination)]
    [InlineData(ForwarderError.ResponseBodyDestination)]
    public void Backend_body_failures_count_even_after_the_request_is_aborted(
    ForwarderError error)
    {
        var outcome = ProxyOutcomeClassifier.Classify(
            200, error, clientAborted: true);

        Assert.True(outcome.IsBackendFailure);
        Assert.Equal("backend_body_io_failed", outcome.Reason);
    }
}

using Yarp.ReverseProxy.Forwarder;

namespace ApiGateway.Resilience;

public static class ProxyOutcomeClassifier
{
    public static ProxyOutcome Classify(
        int statusCode,
        ForwarderError forwarderError,
        bool clientAborted)
    {
        // YARP may abort the request after a destination stream fails.
        if (clientAborted
            && forwarderError != ForwarderError.RequestBodyDestination
            && forwarderError != ForwarderError.ResponseBodyDestination)
        {
            return new(false, "client_canceled");
        }

        return forwarderError switch
        {
            ForwarderError.Request =>
                new(true, "backend_request_failed"),

            ForwarderError.RequestTimedOut =>
                new(true, "downstream_timeout"),

            ForwarderError.RequestBodyDestination
                or ForwarderError.ResponseBodyDestination =>
                new(true, "backend_body_io_failed"),

            ForwarderError.RequestCanceled when statusCode == 504 =>
                new(true, "gateway_timeout"),

            ForwarderError.None => statusCode is 500 or 502 or 503 or 504
                ? new(true, "backend_server_error")
                : new(false, "not_backend_failure"),

            _ => new(false, "non_backend_forwarder_error")
        };
    }
}

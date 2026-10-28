using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IRequestReplyClient
{
    Task<TResponse> RequestAsync<TRequest, TResponse>(string queueName, TRequest request,
        CancellationToken cancellationToken = default);
}

using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Messages;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderMessagePublisher
{
    Task PublishCreateOrderAsync(CreateOrderCommand command);
}

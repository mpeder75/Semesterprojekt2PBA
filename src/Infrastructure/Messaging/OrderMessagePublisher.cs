using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messages;
using RabbitMQ.Client;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class OrderMessagePublisher : IOrderMessagePublisher
{
    private readonly IConnection _connection;
    private const string QueueName = "order.create";

    public OrderMessagePublisher(IConnection connection)
    {
        _connection = connection;
    }

    public async Task PublishCreateOrderAsync(CreateOrderCommand command)
    {
        await using var channel = await _connection.CreateChannelAsync();

        await channel.QueueDeclareAsync(
            QueueName,
            true,
            false,
            false,
            null
        );

        var body = JsonSerializer.SerializeToUtf8Bytes(command);

        await channel.BasicPublishAsync(string.Empty, QueueName, body);
    }
}

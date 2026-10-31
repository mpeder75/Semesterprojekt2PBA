using BasketService.api.Messaging.Messages;
using BasketService.api.Messaging.Replies;

namespace BasketService.api.Messaging.Registry;

public class BasketMessageRegistry
{
    private readonly IReadOnlyDictionary<string, Type> _messageTypes = new Dictionary<string, Type>(StringComparer.Ordinal) 
    {
        [nameof(AddItemToBasketMessage)] = typeof(AddItemToBasketMessage),
        [nameof(DeleteBasketMessage)] = typeof (DeleteBasketMessage),
        [nameof(SetBasketQuantitiesMessage)] = typeof (SetBasketQuantitiesMessage),
        [nameof(TransferBasketMessage)] = typeof (TransferBasketMessage),
        [nameof(GetBasketByIdMessage)] = typeof (GetBasketByIdMessage),
        [nameof(GetBasketByIdReply)] = typeof (GetBasketByIdReply)
    };

    public Type GetMessageType(string messageType)
    {
        ArgumentException.ThrowIfNullOrEmpty(messageType);

        if(!_messageTypes.TryGetValue(messageType, out var type))
        {
            throw new InvalidOperationException($"Unknown basket message type: { messageType}");
        }

        return type;
    }
}

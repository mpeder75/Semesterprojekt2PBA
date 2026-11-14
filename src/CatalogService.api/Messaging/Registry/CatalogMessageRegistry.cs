using CatalogService.api.Messaging.Messages;
using CatalogService.api.Messaging.Replies;

namespace CatalogService.api.Messaging.Registry;

public class CatalogMessageRegistry
{
    private IReadOnlyDictionary<string, Type> _messageTypes = new Dictionary<string, Type>(StringComparer.Ordinal)
    {
        // OrderService sender strengen "GetCatalogItemsByIds" (checkout-planens navn), uden endelsen "Message"
        ["GetCatalogItemsByIds"] = typeof (GetCatalogItemsByIdsMessage),
        [nameof(GetCatalogItemsByIdsMessage)] = typeof (GetCatalogItemsByIdsMessage),
        [nameof(CatalogItemsReturned)] = typeof (CatalogItemsReturned)
    };

    public Type GetMessageType(string messageType)
    {
        ArgumentException.ThrowIfNullOrEmpty(messageType);

        if(!_messageTypes.TryGetValue(messageType, out var type))
        {
            throw new InvalidOperationException($"Unkown Catalog Message Type: {messageType}");
        }

        return type;
    }
}

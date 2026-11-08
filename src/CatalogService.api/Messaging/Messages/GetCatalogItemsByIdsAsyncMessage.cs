using CatalogService.api.Interfaces;

namespace CatalogService.api.Messaging.Messages;

public sealed record GetCatalogItemsByIdsAsyncMessage(
    int[] catalogItemIds) : ICatalogMessage;


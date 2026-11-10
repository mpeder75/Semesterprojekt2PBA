using CatalogService.api.Interfaces;

namespace CatalogService.api.Messaging.Messages;

public sealed record GetCatalogItemsByIdsMessage(
    int[] catalogItemIds) : ICatalogMessage;


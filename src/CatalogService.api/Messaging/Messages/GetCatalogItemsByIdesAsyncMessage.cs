using CatalogService.api.Interfaces;

namespace CatalogService.api.Messaging.Messages;

public sealed record GetCatalogItemsByIdAsyncMessage(
    int[] ids) : ICatalogMessage;


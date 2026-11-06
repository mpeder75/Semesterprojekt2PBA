using CatalogService.api.Entities;
using CatalogService.api.Interfaces;

namespace CatalogService.api.Messaging.Replies;

public sealed record GetCatalogItemsByIdsAsyncReply(List<CatalogItem> items) : ICatalogReply<List<CatalogItem>>;

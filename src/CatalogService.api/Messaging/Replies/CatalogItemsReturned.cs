using CatalogService.api.DTOs;
using CatalogService.api.Entities;
using CatalogService.api.Interfaces;

namespace CatalogService.api.Messaging.Replies;

public sealed record CatalogItemsReturned(List<CatalogItemDTO> items) : ICatalogReply<List<CatalogItemDTO>>;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Catalog.Contracts;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class HttpCatalogClient : ICatalogClient
{
    private readonly HttpClient _http;
    public HttpCatalogClient(HttpClient http) => _http = http;

    public Task<CatalogPage> GetItemsAsync(int pageIndex, int pageSize, int? brandId, int? typeId, CancellationToken cancellationToken = default)
    {
        CatalogValidation.ValidatePage(pageIndex, pageSize);
        var query = $"catalog/items?pageIndex={pageIndex}&pageSize={pageSize}";
        if (brandId.HasValue) query += $"&brandId={brandId.Value}";
        if (typeId.HasValue) query += $"&typeId={typeId.Value}";
        return ReadAsync<CatalogPage>(new HttpRequestMessage(HttpMethod.Get,
            query), cancellationToken);
    }

    public async Task<CatalogItemDto?> GetItemByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"catalog/items/{id}");
        using var response = await SendAsync(request, cancellationToken);
        return response.StatusCode == HttpStatusCode.NotFound ? null : await ReadBodyAsync<CatalogItemDto>(response, cancellationToken);
    }

    public Task<IReadOnlyList<CatalogItemDto>> GetItemsByIdsAsync(int[] ids, CancellationToken cancellationToken = default) =>
        ReadAsync<IReadOnlyList<CatalogItemDto>>(new HttpRequestMessage(HttpMethod.Post, "catalog/items/lookup")
        { Content = JsonContent.Create(ids) }, cancellationToken);

    public Task<IReadOnlyList<CatalogBrandDto>> GetBrandsAsync(CancellationToken cancellationToken = default) =>
        ReadAsync<IReadOnlyList<CatalogBrandDto>>(new HttpRequestMessage(HttpMethod.Get, "catalog/brands"), cancellationToken);

    public Task<IReadOnlyList<CatalogTypeDto>> GetTypesAsync(CancellationToken cancellationToken = default) =>
        ReadAsync<IReadOnlyList<CatalogTypeDto>>(new HttpRequestMessage(HttpMethod.Get, "catalog/types"), cancellationToken);

    public Task<CatalogItemDto> CreateItemAsync(CatalogItemWrite item, CancellationToken cancellationToken = default) =>
        ReadAsync<CatalogItemDto>(new HttpRequestMessage(HttpMethod.Post, "catalog/items")
        { Content = JsonContent.Create(item) }, cancellationToken);

    public async Task<CatalogItemDto?> UpdateItemAsync(int id, CatalogItemWrite item, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"catalog/items/{id}") { Content = JsonContent.Create(item) };
        using var response = await SendAsync(request, cancellationToken);
        return response.StatusCode == HttpStatusCode.NotFound ? null : await ReadBodyAsync<CatalogItemDto>(response, cancellationToken);
    }

    public async Task<bool> DeleteItemAsync(int id, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"catalog/items/{id}");
        using var response = await SendAsync(request, cancellationToken);
        return response.StatusCode != HttpStatusCode.NotFound;
    }

    private async Task<T> ReadAsync<T>(HttpRequestMessage request, CancellationToken token)
    {
        using (request)
        using (var response = await SendAsync(request, token))
            return await ReadBodyAsync<T>(response, token);
    }

    private static async Task<T> ReadBodyAsync<T>(HttpResponseMessage response, CancellationToken token)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: token)
                ?? throw new System.Text.Json.JsonException("Empty catalog response.");
        }
        catch (System.Text.Json.JsonException ex) { throw new CatalogUnavailableException(ex); }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        try
        {
            var response = await _http.SendAsync(request, token);
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound) return response;
            using (response)
            {
                if (response.StatusCode == HttpStatusCode.Conflict) throw new DuplicateException("A catalog item with this name already exists.");
                if (response.StatusCode == HttpStatusCode.BadRequest) throw new ArgumentException("Catalog rejected the supplied item or query.");
                response.EnsureSuccessStatusCode();
            }
            return response;
        }
        catch (HttpRequestException ex) { throw new CatalogUnavailableException(ex); }
        catch (OperationCanceledException ex) when (!token.IsCancellationRequested) { throw new CatalogUnavailableException(ex); }
    }
}

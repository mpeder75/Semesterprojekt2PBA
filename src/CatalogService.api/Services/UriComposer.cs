using CatalogService.api.Interfaces;

namespace CatalogService.api.Services;

public class UriComposer : IUriComposer
{
    private readonly CatalogSettings _catalogSettings;

    public UriComposer(CatalogSettings catalogSettings) => _catalogSettings = catalogSettings;

    public string ComposePicUri(string uriTemplate)
    {
        var pictureUri = uriTemplate
            .Replace("http://catalogbaseurltobereplaced", _catalogSettings.CatalogBaseUrl)
            .Replace('\\', '/');

        if (pictureUri.StartsWith("images/", StringComparison.OrdinalIgnoreCase))
        {
            var baseUrl = _catalogSettings.CatalogBaseUrl?.TrimEnd('/') ?? string.Empty;
            return $"{baseUrl}/{pictureUri}";
        }

        return pictureUri;
    }
}

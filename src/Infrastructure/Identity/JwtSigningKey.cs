using System;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

public static class JwtSigningKey
{
    public static byte[]? GetConfiguredKey(IConfiguration configuration)
    {
        var value = configuration["Authentication:JwtSigningKey"];
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var key = Convert.FromBase64String(value);
        if (key.Length < 32)
            throw new InvalidOperationException("Authentication:JwtSigningKey must contain at least 32 bytes.");

        return key;
    }

    public static byte[] GetLegacyKey() => Encoding.ASCII.GetBytes(AuthorizationConstants.JWT_SECRET_KEY);
}

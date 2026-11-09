using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class IdentityTokenClaimService : ITokenClaimsService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IConfiguration _configuration;

    public IdentityTokenClaimService(UserManager<ApplicationUser> userManager, IConfiguration configuration)
    {
        _userManager = userManager;
        _configuration = configuration;
    }

    public async Task<string> GetTokenAsync(string userName)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var configuredKey = JwtSigningKey.GetConfiguredKey(_configuration);
        var key = configuredKey ?? JwtSigningKey.GetLegacyKey();
        var user = await _userManager.FindByNameAsync(userName);
        if (user == null) throw new UserNotFoundException(userName);
        var roles = await _userManager.GetRolesAsync(user);
        var claims = new List<Claim> { new Claim(ClaimTypes.Name, userName) };
        const string adminRole = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;
        var usesDemoAdminPassword = configuredKey is not null && roles.Contains(adminRole)
            && await _userManager.CheckPasswordAsync(user, AuthorizationConstants.DEFAULT_PASSWORD);

        foreach (var role in roles)
        {
            if (role == adminRole && usesDemoAdminPassword)
                continue;
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims.ToArray()),
            Expires = DateTime.UtcNow.AddDays(7),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }
}

using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Identity;

public class IdentityTokenClaimServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfiguredKeySignsAdminClaimsOnlyForPrivatePasswords(bool usesDemoPassword)
    {
        const string adminRole = Constants.Roles.ADMINISTRATORS;
        var key = RandomNumberGenerator.GetBytes(32);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:JwtSigningKey"] = Convert.ToBase64String(key)
        }).Build();
        var user = new ApplicationUser { UserName = "admin" };
        using var users = Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);
        users.FindByNameAsync("admin").Returns(user);
        users.GetRolesAsync(user).Returns(new List<string> { adminRole });
        users.CheckPasswordAsync(user, AuthorizationConstants.DEFAULT_PASSWORD).Returns(usesDemoPassword);

        var token = await new IdentityTokenClaimService(users, configuration).GetTokenAsync("admin");
        var principal = new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ValidateIssuer = false,
            ValidateAudience = false
        }, out _);

        Assert.Equal(!usesDemoPassword, principal.IsInRole(adminRole));
    }
}

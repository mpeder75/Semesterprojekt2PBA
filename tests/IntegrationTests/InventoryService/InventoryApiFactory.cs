#nullable enable

using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.InventoryService;

public sealed class InventoryApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private static readonly byte[] SigningKey = RandomNumberGenerator.GetBytes(32);
    private readonly string _databaseName = $"InventoryTests_{Guid.NewGuid():N}";
    public string ConnectionString { get; }

    public InventoryApiFactory()
    {
        var connection = new SqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("INVENTORY_TEST_SQL")
            ?? @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true;")
        {
            InitialCatalog = _databaseName
        };
        ConnectionString = connection.ConnectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:InventoryConnection"] = ConnectionString,
                ["Authentication:JwtSigningKey"] = Convert.ToBase64String(SigningKey),
                ["Logging:LogLevel:Default"] = "Warning"
            }));
    }

    internal HttpClient CreateAdminClient() => CreateAdminClient(this);

    internal static HttpClient CreateAdminClient(WebApplicationFactory<Program> application)
    {
        var client = application.CreateClient();
        client.DefaultRequestHeaders.Authorization = CreateAuthorization("Administrators");
        return client;
    }

    internal static AuthenticationHeaderValue CreateAuthorization(string role, bool validSignature = true)
    {
        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "inventory-test-user"),
                new Claim(ClaimTypes.Role, role)
            }),
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(validSignature ? SigningKey : RandomNumberGenerator.GetBytes(32)),
                SecurityAlgorithms.HmacSha256Signature)
        });
        return new AuthenticationHeaderValue("Bearer", handler.WriteToken(token));
    }

    public async Task InitializeAsync()
    {
        // Fail clearly when SQL Server is unavailable instead of silently skipping the suite.
        var master = new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" };
        await using var connection = new SqlConnection(master.ConnectionString);
        await connection.OpenAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        SqlConnection.ClearAllPools();

        var master = new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" };
        await using var connection = new SqlConnection(master.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        // The name is generated here, never accepted from configuration or a caller.
        command.CommandText = $"""
            IF DB_ID(N'{_databaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{_databaseName}];
            END
            """;
        await command.ExecuteNonQueryAsync();
    }
}

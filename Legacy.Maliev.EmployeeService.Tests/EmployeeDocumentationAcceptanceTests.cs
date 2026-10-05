using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace Legacy.Maliev.EmployeeService.Tests;

public sealed class EmployeeDocumentationAcceptanceTests(EmployeeRouteAcceptanceFixture fixture)
    : IClassFixture<EmployeeRouteAcceptanceFixture>
{
    [Theory]
    [InlineData("Development", true)]
    [InlineData("Staging", true)]
    [InlineData("Production", false)]
    public async Task Documentation_RespectsEnvironmentBoundary(string environment, bool exposed)
    {
        await using var host = fixture.Factory.WithWebHostBuilder(builder => builder.UseEnvironment(environment));
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync("/employee/openapi/v1.json");
        Assert.Equal(exposed ? HttpStatusCode.OK : HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Documentation_ConsumesMaintainedSummariesAndAdvertisesExistingBearerRequirement()
    {
        await using var host = fixture.Factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync("/employee/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var count = 0;
        foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            if (!path.Name.StartsWith("/Employees", StringComparison.OrdinalIgnoreCase)
                && !path.Name.StartsWith("/employees/addresses", StringComparison.OrdinalIgnoreCase)
                && !path.Name.StartsWith("/employees/roles", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var operation in path.Value.EnumerateObject().Where(item => item.Name is "get" or "post" or "put" or "delete"))
            {
                count++;
                Assert.True(operation.Value.TryGetProperty("summary", out var summary) && !string.IsNullOrWhiteSpace(summary.GetString()),
                    $"Missing maintained summary for {path.Name} {operation.Name}");
                Assert.True(operation.Value.TryGetProperty("security", out var security) && security.GetArrayLength() > 0,
                    $"Missing existing bearer requirement for {path.Name} {operation.Name}");
            }
        }
        Assert.True(count >= 5);
        var bearer = document.RootElement.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
    }

    [Fact]
    public async Task Documentation_DescribesActualAddressCreationAndMutationResults()
    {
        await using var host = fixture.Factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var document = JsonDocument.Parse(await client.GetStringAsync("/employee/openapi/v1.json"));
        var paths = document.RootElement.GetProperty("paths");
        var created = paths.GetProperty("/employees/Addresses").GetProperty("post").GetProperty("responses").GetProperty("201");
        Assert.True(created.GetProperty("content").TryGetProperty("application/json", out _));
        Assert.False(string.IsNullOrWhiteSpace(created.GetProperty("description").GetString()));
        foreach (var method in new[] { "put", "delete" })
            foreach (var status in new[] { "204", "404" })
                Assert.False(string.IsNullOrWhiteSpace(paths.GetProperty("/employees/Addresses/{addressId}").GetProperty(method).GetProperty("responses").GetProperty(status).GetProperty("description").GetString()));
    }

    [Fact]
    public async Task Documentation_ExplainsProfileReferencesAndAddressPayload()
    {
        await using var host = fixture.Factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var document = JsonDocument.Parse(await client.GetStringAsync("/employee/openapi/v1.json"));
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var address = schemas.GetProperty("UpsertAddressRequest");
        Assert.Equal("Employee address create/update request.", address.GetProperty("description").GetString());
        foreach (var name in new[] { "Building", "AddressLine1", "AddressLine2", "City", "State", "PostalCode", "CountryId" })
            Assert.True(address.GetProperty("properties").GetProperty(name).TryGetProperty("description", out var description)
                && !string.IsNullOrWhiteSpace(description.GetString()), $"Missing address field guidance for {name}: {address}");
        var profile = schemas.GetProperty("EmployeeResponse").GetProperty("properties");
        foreach (var name in new[] { "HomeAddress", "Role" })
            Assert.True(profile.GetProperty(name).TryGetProperty("description", out var description)
                && !string.IsNullOrWhiteSpace(description.GetString()), $"Missing profile reference guidance for {name}: {profile}");
    }
}

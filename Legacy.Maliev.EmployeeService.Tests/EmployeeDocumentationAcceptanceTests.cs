using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace Legacy.Maliev.EmployeeService.Tests;

/// <summary>Checks generated OpenAPI guidance against maintained Employee routes, schemas, and authorization behavior.</summary>
/// <param name="fixture">The shared production Employee host, PostgreSQL, Redis, and controlled authorization fixture used by these assertions.</param>
public sealed class EmployeeDocumentationAcceptanceTests(EmployeeRouteAcceptanceFixture fixture)
    : IClassFixture<EmployeeRouteAcceptanceFixture>
{
    /// <summary>Checks that OpenAPI is exposed in development and staging and hidden in production.</summary>
    /// <param name="environment">The ASP.NET Core environment used for the documentation request.</param>
    /// <param name="exposed">Whether the OpenAPI endpoint is expected to be available in that environment.</param>
    /// <returns>A task that completes after verifying that OpenAPI is exposed in development and staging and hidden in production.</returns>
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

    /// <summary>Checks maintained operation summaries and bearer requirements in generated Employee OpenAPI.</summary>
    /// <returns>A task that completes after verifying maintained operation summaries and bearer requirements in generated Employee OpenAPI.</returns>
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

    /// <summary>Checks documented address creation JSON and existing success and not-found mutation responses.</summary>
    /// <returns>A task that completes after verifying documented address creation JSON and existing success and not-found mutation responses.</returns>
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

    /// <summary>Checks guidance for writable address fields and nullable employee address and role references.</summary>
    /// <returns>A task that completes after verifying guidance for writable address fields and nullable employee address and role references.</returns>
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
        foreach (var (name, target) in new[] { ("HomeAddress", "AddressResponse"), ("Role", "RoleResponse") })
        {
            var alternatives = profile.GetProperty(name).GetProperty("oneOf").EnumerateArray().ToArray();
            Assert.Equal(2, alternatives.Length);
            Assert.Single(alternatives, item => item.TryGetProperty("type", out var type) && type.GetString() == "null");
            var reference = Assert.Single(alternatives, item => item.TryGetProperty("$ref", out _));
            Assert.Equal($"#/components/schemas/{target}", reference.GetProperty("$ref").GetString());
            Assert.True(reference.TryGetProperty("description", out var description)
                && !string.IsNullOrWhiteSpace(description.GetString()), $"Missing profile reference guidance for {name}: {profile}");
        }
    }

    /// <summary>Checks that address-deletion documentation describes the existing fresh live authorization requirement.</summary>
    /// <returns>A task that completes after verifying that address-deletion documentation describes the existing fresh live authorization requirement.</returns>
    [Fact]
    public async Task Documentation_ExplainsExistingLiveAuthorizationForCriticalAddressDeletion()
    {
        await using var host = fixture.Factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var document = JsonDocument.Parse(await client.GetStringAsync("/employee/openapi/v1.json"));
        var deletion = document.RootElement.GetProperty("paths").GetProperty("/employees/Addresses/{addressId}").GetProperty("delete");
        Assert.True(deletion.TryGetProperty("description", out var description), "The existing live authorization requirement must be documented.");
        Assert.Equal("Deletion requires a fresh authorization decision for this address; cached permission claims do not authorize this critical operation.", description.GetString());
    }

    /// <summary>Checks directory query names and documentation of complete omitted-size cohorts, zero pages, and negative provider boundaries.</summary>
    /// <returns>A task that completes after verifying directory query names and documentation of complete omitted-size cohorts, zero pages, and negative provider boundaries.</returns>
    [Fact]
    public async Task Documentation_ExplainsExistingDirectoryDefaultsAndQueryNames()
    {
        await using var host = fixture.Factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var document = JsonDocument.Parse(await client.GetStringAsync("/employee/openapi/v1.json"));
        var parameters = document.RootElement.GetProperty("paths").GetProperty("/Employees").GetProperty("get").GetProperty("parameters").EnumerateArray().ToArray();
        foreach (var name in new[] { "sort", "search", "index", "size" })
        {
            var parameter = Assert.Single(parameters, item => item.GetProperty("name").GetString() == name);
            Assert.True(parameter.TryGetProperty("description", out var description) && !string.IsNullOrWhiteSpace(description.GetString()),
                $"Missing existing directory query guidance for {name}: {parameter}");
        }
        var size = Assert.Single(parameters, item => item.GetProperty("name").GetString() == "size");
        // Preserve this original documentation test identity and host/query checks.
        // Its size description must describe the committed source fidelity contract.
        var sizeDescription = size.GetProperty("description").GetString();
        Assert.Contains("omitted or empty values return the complete filtered cohort without a row cap", sizeDescription, StringComparison.Ordinal);
        Assert.Contains("Explicit numeric values are forwarded without a cap or lower clamp", sizeDescription, StringComparison.Ordinal);
        Assert.Contains("zero returns an empty page (404)", sizeDescription, StringComparison.Ordinal);
        Assert.Contains("negative values retain the original provider failure boundary", sizeDescription, StringComparison.Ordinal);
        Assert.DoesNotContain("default to 50", sizeDescription, StringComparison.Ordinal);
        Assert.DoesNotContain("bounded from 1 to 250", sizeDescription, StringComparison.Ordinal);
        var sort = Assert.Single(parameters, item => item.GetProperty("name").GetString() == "sort");
        Assert.Equal("EmployeeId_Ascending", sort.GetProperty("example").GetString());
    }

    /// <summary>Checks that address request-body documentation describes payload fields rather than cancellation tokens.</summary>
    /// <returns>A task that completes after verifying that address request-body documentation describes payload fields rather than cancellation tokens.</returns>
    [Fact]
    public async Task Documentation_DescribesAddressPayloadInsteadOfCancellation()
    {
        await using var host = fixture.Factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var document = JsonDocument.Parse(await client.GetStringAsync("/employee/openapi/v1.json"));
        foreach (var (path, method) in new[] { ("/employees/Addresses", "post"), ("/employees/Addresses/{addressId}", "put") })
        {
            var body = document.RootElement.GetProperty("paths").GetProperty(path).GetProperty(method).GetProperty("requestBody");
            Assert.True(body.TryGetProperty("description", out var description));
            Assert.Contains("address fields", description.GetString(), StringComparison.Ordinal);
            Assert.DoesNotContain("cancellation", description.GetString(), StringComparison.OrdinalIgnoreCase);
        }
    }
}

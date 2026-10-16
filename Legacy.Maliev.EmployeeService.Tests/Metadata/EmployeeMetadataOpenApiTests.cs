using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

using MvcJsonOptions = Microsoft.AspNetCore.Mvc.JsonOptions;

namespace Legacy.Maliev.EmployeeService.Tests.Metadata;

[Collection("Employee scaffold runtime")]
public sealed class EmployeeMetadataOpenApiTests(EmployeeRouteAcceptanceFixture fixture, ITestOutputHelper output)
    : IClassFixture<EmployeeRouteAcceptanceFixture>
{
    private const string ConverterPrefix = "Legacy.Maliev.EmployeeService.Api.Serialization.EmployeeConsumedStringRequestJsonConverter";

    [Theory]
    [InlineData("UpsertAddressRequest")]
    [InlineData("UpsertSignatureImageFileRequest")]
    public async Task ActualDocument_KeepsNamedRequestSchemaAndNativeRequiredNullability(string schemaName)
    {
        await using var candidate = fixture.Factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        await using var native = fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services => services.PostConfigure<MvcJsonOptions>(options =>
            {
                for (var index = options.JsonSerializerOptions.Converters.Count - 1; index >= 0; index--)
                    if (options.JsonSerializerOptions.Converters[index].GetType().FullName?.StartsWith(
                        ConverterPrefix, StringComparison.Ordinal) == true)
                        options.JsonSerializerOptions.Converters.RemoveAt(index);
            }));
        });
        using var candidateClient = candidate.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var nativeClient = native.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var candidateDocument = await DocumentAsync(candidateClient);
        var nativeDocument = await DocumentAsync(nativeClient);
        var actual = Projection(candidateDocument, schemaName);
        var expected = Projection(nativeDocument, schemaName);
        output.WriteLine("METADATA-SCHEMA-NATIVE " + expected);
        output.WriteLine("METADATA-SCHEMA-CANDIDATE " + actual);
        Assert.Equal(expected, actual);
    }

    private static async Task<JsonElement> DocumentAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/employee/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static string Projection(JsonElement document, string schemaName)
    {
        var schema = document.GetProperty("components").GetProperty("schemas").GetProperty(schemaName);
        Assert.True(schema.TryGetProperty("properties", out var properties),
            $"Request schema {schemaName} must not become opaque.");
        string[] names = schemaName == "UpsertAddressRequest"
            ? ["Building", "AddressLine1", "AddressLine2", "City", "State", "PostalCode", "CountryId"]
            : ["Bucket", "ObjectName", "EmployeeId"];
        Assert.Equal(names.Order(StringComparer.Ordinal).ToArray(),
            properties.EnumerateObject().Select(value => value.Name).Order(StringComparer.Ordinal).ToArray());
        var required = schema.TryGetProperty("required", out var requiredNode)
            ? requiredNode.EnumerateArray().Select(value => value.GetString()!).Order(StringComparer.Ordinal).ToArray()
            : Array.Empty<string>();
        Assert.All(required, name => Assert.Contains(name, names));
        var rows = names.Order(StringComparer.Ordinal).Select(name =>
        {
            var property = properties.GetProperty(name);
            var types = Types(property).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            Assert.Contains(name is "CountryId" or "EmployeeId" ? "integer" : "string", types);
            return new
            {
                Name = name,
                Types = types,
                Nullable = types.Contains("null", StringComparer.Ordinal),
                Required = required.Contains(name, StringComparer.Ordinal),
                Schema = Canonical(property),
            };
        }).ToArray();
        // Requiredness/nullability are measured from the real native document, not
        // guessed from parameter defaults or patched schema metadata.
        return JsonSerializer.Serialize(new { Name = schemaName, Required = required, Properties = rows });
    }

    private static IEnumerable<string> Types(JsonElement schema)
    {
        if (schema.TryGetProperty("type", out var type))
        {
            if (type.ValueKind == JsonValueKind.String) yield return type.GetString()!;
            else if (type.ValueKind == JsonValueKind.Array)
                foreach (var value in type.EnumerateArray()) yield return value.GetString()!;
        }
        if (schema.TryGetProperty("nullable", out var nullable) && nullable.ValueKind == JsonValueKind.True)
            yield return "null";
        foreach (var keyword in new[] { "oneOf", "anyOf", "allOf" })
            if (schema.TryGetProperty(keyword, out var alternatives))
                foreach (var alternative in alternatives.EnumerateArray())
                    foreach (var value in Types(alternative)) yield return value;
    }

    private static string Canonical(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(",", value.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal)
            .Select(item => JsonSerializer.Serialize(item.Name) + ":" + Canonical(item.Value))) + "}",
        JsonValueKind.Array => "[" + string.Join(",", value.EnumerateArray().Select(Canonical)) + "]",
        JsonValueKind.String => JsonSerializer.Serialize(value.GetString()),
        _ => value.GetRawText(),
    };
}

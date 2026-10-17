using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Legacy.Maliev.EmployeeService.Application.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

using MvcJsonOptions = Microsoft.AspNetCore.Mvc.JsonOptions;

namespace Legacy.Maliev.EmployeeService.Tests.Metadata;

[Collection("Employee scaffold runtime")]
public sealed class EmployeeMetadataOpenApiTests(EmployeeRouteAcceptanceFixture fixture, ITestOutputHelper output)
    : IClassFixture<EmployeeRouteAcceptanceFixture>
{
    private const string ResolverName = "Legacy.Maliev.EmployeeService.Api.Serialization.EmployeeMetadataRequestJsonTypeInfoResolver";
    private const string PropertyReaderName = "Legacy.Maliev.EmployeeService.Api.Serialization.EmployeeConsumedMetadataStringJsonConverter";

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
                RemoveOwnedResolver(options.JsonSerializerOptions);
            }));
        });
        using var candidateClient = candidate.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var nativeClient = native.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        AssertNativeScope(native.Services.GetRequiredService<IOptions<MvcJsonOptions>>().Value.JsonSerializerOptions);
        var candidateDocument = await DocumentAsync(candidateClient);
        var nativeDocument = await DocumentAsync(nativeClient);
        var actual = Projection(candidateDocument, schemaName);
        var expected = Projection(nativeDocument, schemaName);
        output.WriteLine("METADATA-SCHEMA-NATIVE " + expected);
        output.WriteLine("METADATA-SCHEMA-CANDIDATE " + actual);
        Assert.Equal(expected, actual);
    }

    private static bool IsOwnedResolver(IJsonTypeInfoResolver resolver) => resolver.GetType().FullName == ResolverName;
    private static void RemoveOwnedResolver(JsonSerializerOptions options)
    {
        var before = options.TypeInfoResolverChain.ToArray();
        var owned = before.Select((resolver, index) => (resolver, index))
            .Where(item => IsOwnedResolver(item.resolver)).ToArray();
        Assert.InRange(owned.Length, 0, 1);
        var expected = before.ToArray();
        foreach (var (resolver, index) in owned)
        {
            var inner = resolver.GetType().GetProperty("Inner");
            Assert.NotNull(inner);
            expected[index] = Assert.IsAssignableFrom<IJsonTypeInfoResolver>(inner.GetValue(resolver));
            options.TypeInfoResolverChain[index] = expected[index];
        }
        Assert.Equal(before.Length, options.TypeInfoResolverChain.Count);
        for (var index = 0; index < before.Length; index++) Assert.Same(expected[index], options.TypeInfoResolverChain[index]);
        Assert.DoesNotContain(options.TypeInfoResolverChain, IsOwnedResolver);
    }
    private static void AssertNativeScope(JsonSerializerOptions options)
    {
        Assert.DoesNotContain(options.TypeInfoResolverChain, IsOwnedResolver);
        foreach (var type in new[] { typeof(UpsertAddressRequest), typeof(UpsertSignatureImageFileRequest) })
        {
            var metadata = options.GetTypeInfo(type);
            Assert.Equal(JsonTypeInfoKind.Object, metadata.Kind);
            Assert.Null(metadata.CreateObject);
            Assert.DoesNotContain(metadata.Properties, property =>
                property.CustomConverter?.GetType().FullName == PropertyReaderName);
        }
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

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
    [Theory]
    [InlineData("UpsertAddressRequest")]
    [InlineData("UpsertSignatureImageFileRequest")]
    public async Task ActualDocument_KeepsNamedRequestSchemaAndNativeRequiredNullability(string schemaName)
    {
        await using var candidate = fixture.Factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        await using var native = fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(EmployeeMetadataNativeOptions.RemoveOwnedConfiguration);
        });
        using var candidateClient = candidate.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var nativeClient = native.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        EmployeeMetadataNativeOptions.AssertNativeScope(native.Services.GetRequiredService<IOptions<MvcJsonOptions>>().Value.JsonSerializerOptions);
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

internal static class EmployeeMetadataNativeOptions
{
    private const string ConfigurationName = "Legacy.Maliev.EmployeeService.Api.Serialization.EmployeeMetadataJsonOptions";
    private const string PropertyReaderName = "Legacy.Maliev.EmployeeService.Api.Serialization.EmployeeConsumedMetadataStringJsonConverter";

    internal static bool IsOwnedConfiguration(IConfigureOptions<MvcJsonOptions> configuration)
        => configuration is ConfigureNamedOptions<MvcJsonOptions> { Name: "", Action: { } action }
            && action.Target is null
            && action.GetInvocationList().Length == 1
            && action.Method.Name == "Configure"
            && action.Method.DeclaringType?.FullName == ConfigurationName
            && action.Method.DeclaringType?.Assembly == typeof(Program).Assembly;

    private static bool IsAddressInputConfiguration(IConfigureOptions<MvcJsonOptions> configuration)
        => configuration is ConfigureNamedOptions<MvcJsonOptions> { Name: "", Action: { } action }
            && action.Target is null
            && action.GetInvocationList().Length == 1
            && action.Method.Name == "Configure"
            && action.Method.DeclaringType?.FullName ==
                "Legacy.Maliev.EmployeeService.Api.Serialization.EmployeeAddressInputJsonOptions"
            && action.Method.DeclaringType?.Assembly == typeof(Program).Assembly;

    private static bool IsCompatibilityConfiguration(IConfigureOptions<MvcJsonOptions> configuration)
        => IsOwnedConfiguration(configuration) || IsAddressInputConfiguration(configuration);

    internal static JsonSerializerOptions Create(IServiceProvider services)
    {
        var source = services.GetServices<IConfigureOptions<MvcJsonOptions>>().ToArray();
        var owned = source.Where(IsCompatibilityConfiguration).ToArray();
        Assert.Equal(typeof(Program).Assembly.GetType(ConfigurationName, throwOnError: false) is null ? 0 : 1,
            source.Count(IsOwnedConfiguration));
        Assert.Equal(typeof(Program).Assembly.GetType(
            "Legacy.Maliev.EmployeeService.Api.Serialization.EmployeeAddressInputJsonOptions", throwOnError: false) is null ? 0 : 1,
            source.Count(IsAddressInputConfiguration));
        var retained = source.Where(configuration => !IsCompatibilityConfiguration(configuration)).ToArray();
        Assert.Equal(source.Length - owned.Length, retained.Length);
        var index = 0;
        foreach (var configuration in source)
        {
            if (IsCompatibilityConfiguration(configuration)) continue;
            Assert.Same(configuration, retained[index++]);
        }
        // Recreate fresh MVC options through every other original setup, post-setup,
        // and validator. Never clone metadata already changed by the owned reader.
        var factory = new OptionsFactory<MvcJsonOptions>(retained,
            services.GetServices<IPostConfigureOptions<MvcJsonOptions>>(),
            services.GetServices<IValidateOptions<MvcJsonOptions>>());
        return factory.Create(Microsoft.Extensions.Options.Options.DefaultName).JsonSerializerOptions;
    }

    internal static void RemoveOwnedConfiguration(IServiceCollection services)
    {
        var before = services.ToArray();
        var owned = before.Where(descriptor =>
            descriptor.ServiceType == typeof(IConfigureOptions<MvcJsonOptions>)
            && descriptor.ImplementationInstance is IConfigureOptions<MvcJsonOptions> configuration
            && IsCompatibilityConfiguration(configuration)).ToArray();
        Assert.Single(owned, descriptor => descriptor.ImplementationInstance is IConfigureOptions<MvcJsonOptions> configuration
            && IsOwnedConfiguration(configuration));
        Assert.Equal(typeof(Program).Assembly.GetType(
            "Legacy.Maliev.EmployeeService.Api.Serialization.EmployeeAddressInputJsonOptions", throwOnError: false) is null ? 0 : 1,
            owned.Count(descriptor => descriptor.ImplementationInstance is IConfigureOptions<MvcJsonOptions> configuration
                && IsAddressInputConfiguration(configuration)));
        foreach (var descriptor in owned) Assert.True(services.Remove(descriptor));
        var retained = before.Where(descriptor => !owned.Any(item => ReferenceEquals(descriptor, item))).ToArray();
        Assert.Equal(retained.Length, services.Count);
        for (var index = 0; index < retained.Length; index++) Assert.Same(retained[index], services[index]);
    }

    internal static void AssertNativeScope(JsonSerializerOptions options)
    {
        foreach (var type in new[] { typeof(UpsertAddressRequest), typeof(UpsertSignatureImageFileRequest) })
        {
            var metadata = options.GetTypeInfo(type);
            Assert.Equal(JsonTypeInfoKind.Object, metadata.Kind);
            Assert.Null(metadata.CreateObject);
            Assert.DoesNotContain(metadata.Properties, property =>
                property.CustomConverter?.GetType().FullName == PropertyReaderName);
        }
    }
}

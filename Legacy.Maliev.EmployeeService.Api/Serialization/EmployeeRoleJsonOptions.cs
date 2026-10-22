using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Legacy.Maliev.EmployeeService.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace Legacy.Maliev.EmployeeService.Api.Serialization;

/// <summary>Retains source string-binding semantics only for the consumed Role request members.</summary>
public static class EmployeeRoleJsonOptions
{
    /// <summary>Wraps native MVC metadata without replacing traversal, response writing or unrelated requests.</summary>
    /// <param name="options">The existing MVC JSON options.</param>
    public static void Configure(JsonOptions options)
    {
        var serializer = options.JsonSerializerOptions;
        if (serializer.TypeInfoResolver is EmployeeRoleRequestJsonTypeInfoResolver) return;
        serializer.TypeInfoResolver = new EmployeeRoleRequestJsonTypeInfoResolver(
            serializer.TypeInfoResolver ?? throw new InvalidOperationException("MVC JSON metadata resolver is required."));
    }
}

internal sealed class EmployeeRoleRequestJsonTypeInfoResolver(IJsonTypeInfoResolver inner) : IJsonTypeInfoResolver
{
    private static readonly JsonConverter<string> TextReader = new EmployeeRoleStringJsonConverter();

    public IJsonTypeInfoResolver Inner { get; } = inner;

    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        var metadata = Inner.GetTypeInfo(type, options);
        if (type != typeof(UpsertRoleRequest)) return metadata;
        if (metadata is null || metadata.Kind != JsonTypeInfoKind.Object)
            throw new InvalidOperationException("Native Role request object contract is required.");

        metadata.CreateObject = static () => new UpsertRoleRequest(null, null);
        var configured = 0;
        foreach (var property in metadata.Properties)
        {
            if (property.Name is not ("Name" or "Description")) continue;
            if (property.PropertyType != typeof(string) || property.Set is not { } setter)
                throw new InvalidOperationException("Consumed Role string setter is required.");
            property.CustomConverter = TextReader;
            property.Set = (target, value) =>
            {
                if (value is not null) setter(target, value);
            };
            configured++;
        }
        if (configured != 2)
            throw new InvalidOperationException("Exact consumed Role property set is required.");
        return metadata;
    }
}

// Deliberately distinct from the Address/Signature property-reader type so their
// owned-layer isolation controls remain exact. Primitive decoding is shared,
// not installed as a global string or whole-object converter.
internal sealed class EmployeeRoleStringJsonConverter : JsonConverter<string>
{
    private static readonly EmployeeConsumedMetadataStringJsonConverter Reader = new();

    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => Reader.Read(ref reader, typeToConvert, options);

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        => Reader.Write(writer, value, options);
}

using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Legacy.Maliev.EmployeeService.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace Legacy.Maliev.EmployeeService.Api.Serialization;

/// <summary>Registers compatibility readers only for consumed employee metadata request strings.</summary>
public static class EmployeeMetadataJsonOptions
{
    /// <summary>Configures MVC property readers without replacing native DTO traversal or response contracts.</summary>
    /// <param name="options">The existing MVC JSON options.</param>
    public static void Configure(JsonOptions options)
    {
        var serializer = options.JsonSerializerOptions;
        if (serializer.TypeInfoResolver is EmployeeMetadataRequestJsonTypeInfoResolver) return;
        serializer.TypeInfoResolver = new EmployeeMetadataRequestJsonTypeInfoResolver(
            serializer.TypeInfoResolver ?? throw new InvalidOperationException("MVC JSON metadata resolver is required."));
    }
}

internal sealed class EmployeeMetadataRequestJsonTypeInfoResolver(IJsonTypeInfoResolver inner) : IJsonTypeInfoResolver
{
    private static readonly HashSet<string> AddressFields = new(
        ["Building", "AddressLine1", "AddressLine2", "City", "State", "PostalCode"], StringComparer.Ordinal);
    private static readonly HashSet<string> SignatureFields = new(["Bucket", "ObjectName"], StringComparer.Ordinal);
    private static readonly JsonConverter<string> TextReader = new EmployeeConsumedMetadataStringJsonConverter();

    // Exposed for exact owned-layer removal by source-compatible native comparison tests.
    public IJsonTypeInfoResolver Inner { get; } = inner;

    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        var metadata = Inner.GetTypeInfo(type, options);
        if (type != typeof(UpsertAddressRequest) && type != typeof(UpsertSignatureImageFileRequest))
            return metadata;
        if (metadata is null || metadata.Kind != JsonTypeInfoKind.Object)
            throw new InvalidOperationException("Native metadata request object contract is required.");

        var address = type == typeof(UpsertAddressRequest);
        var names = address ? AddressFields : SignatureFields;
        // Native omitted constructor-argument defaults: six null strings/CountryId0,
        // or two null strings/nullable EmployeeId. Only the eight consumed strings skip null.
        metadata.CreateObject = address
            ? static () => new UpsertAddressRequest(null, null, null, null, null, null, 0)
            : static () => new UpsertSignatureImageFileRequest(null!, null!, null);
        var configured = 0;
        foreach (var property in metadata.Properties)
        {
            if (!names.Contains(property.Name)) continue;
            if (property.PropertyType != typeof(string) || property.Set is not { } setter)
                throw new InvalidOperationException("Consumed metadata string setter is required.");
            property.CustomConverter = TextReader;
            property.Set = (target, value) =>
            {
                if (value is not null) setter(target, value);
            };
            configured++;
        }
        if (configured != names.Count)
            throw new InvalidOperationException("Exact consumed metadata property set is required.");
        return metadata;
    }
}

internal sealed class EmployeeConsumedMetadataStringJsonConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.String:
                return reader.GetString();
            case JsonTokenType.True:
                return "true";
            case JsonTokenType.False:
                return "false";
            case JsonTokenType.Number:
                var lexeme = reader.HasValueSequence
                    ? Encoding.UTF8.GetString(reader.ValueSequence.ToArray())
                    : Encoding.UTF8.GetString(reader.ValueSpan);
                if (double.TryParse(lexeme, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    return lexeme;
                break;
        }
        // Native object traversal supplies the current property path. Never fabricate
        // a Path or pre-buffer/replay the request through the ref-reader overload.
        throw new JsonException("Expected a compatible metadata string primitive.");
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}

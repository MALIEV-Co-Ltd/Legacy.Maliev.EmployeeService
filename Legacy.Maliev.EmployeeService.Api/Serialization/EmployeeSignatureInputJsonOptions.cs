using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Legacy.Maliev.EmployeeService.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace Legacy.Maliev.EmployeeService.Api.Serialization;

/// <summary>Validates source Signature identity and audit inputs without assigning them to the request DTO.</summary>
public static class EmployeeSignatureInputJsonOptions
{
    /// <summary>Adds read-only validation to MVC Signature request metadata, preserving native outer traversal.</summary>
    /// <param name="options">The existing MVC JSON options.</param>
    public static void Configure(JsonOptions options)
    {
        var serializer = options.JsonSerializerOptions;
        if (serializer.TypeInfoResolver is EmployeeSignatureInputJsonTypeInfoResolver) return;
        serializer.TypeInfoResolver = new EmployeeSignatureInputJsonTypeInfoResolver(
            serializer.TypeInfoResolver ?? throw new InvalidOperationException("MVC metadata resolver is required."));
    }
}

internal sealed class EmployeeSignatureInputJsonTypeInfoResolver(IJsonTypeInfoResolver inner) : IJsonTypeInfoResolver
{
    private static readonly JsonConverter<int?> IdentityReader = new EmployeeSignatureInt32InputConverter();
    private static readonly JsonConverter<DateTime?> AuditReader = new EmployeeSignatureDateTimeInputConverter();

    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        var metadata = inner.GetTypeInfo(type, options);
        if (type != typeof(UpsertSignatureImageFileRequest)) return metadata;
        if (metadata is null || metadata.Kind != JsonTypeInfoKind.Object)
            throw new InvalidOperationException("Native Signature object metadata is required.");
        Add(metadata, "Id", typeof(int?), IdentityReader);
        Add(metadata, "CreatedDate", typeof(DateTime?), AuditReader);
        Add(metadata, "ModifiedDate", typeof(DateTime?), AuditReader);
        return metadata;
    }

    private static void Add(JsonTypeInfo metadata, string name, Type type, JsonConverter converter)
    {
        if (metadata.Properties.Any(property => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Source-validation metadata must not replace a real DTO member.");
        var property = metadata.CreateJsonPropertyInfo(type, name);
        property.CustomConverter = converter;
        property.Set = static (_, _) => { };
        property.Get = null;
        property.ShouldSerialize = static (_, _) => false;
        property.IsRequired = false;
        metadata.Properties.Add(property);
    }
}

internal sealed class EmployeeSignatureInt32InputConverter : JsonConverter<int?>
{
    public override bool HandleNull => true;

    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            return EmployeeAddressInputReader.ReadInt32(ref reader);
        }
        catch (JsonException)
        {
            throw new JsonException("Invalid Signature identity metadata.");
        }
    }

    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
        => throw new NotSupportedException("Virtual Signature input metadata is never written.");
}

internal sealed class EmployeeSignatureDateTimeInputConverter : JsonConverter<DateTime?>
{
    public override bool HandleNull => true;

    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            return EmployeeAddressInputReader.ReadDateTime(ref reader);
        }
        catch (JsonException)
        {
            throw new JsonException("Invalid Signature audit metadata.");
        }
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
        => throw new NotSupportedException("Virtual Signature input metadata is never written.");
}

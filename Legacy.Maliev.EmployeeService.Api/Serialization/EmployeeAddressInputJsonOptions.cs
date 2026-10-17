using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Legacy.Maliev.EmployeeService.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace Legacy.Maliev.EmployeeService.Api.Serialization;

/// <summary>Validates source Address identity and audit inputs without assigning them to the request DTO.</summary>
public static class EmployeeAddressInputJsonOptions
{
    /// <summary>Adds read-only validation to MVC Address request metadata, preserving native outer traversal.</summary>
    /// <param name="options">The existing MVC JSON options.</param>
    public static void Configure(JsonOptions options)
    {
        var serializer = options.JsonSerializerOptions;
        if (serializer.TypeInfoResolver is EmployeeAddressInputJsonTypeInfoResolver) return;
        serializer.TypeInfoResolver = new EmployeeAddressInputJsonTypeInfoResolver(
            serializer.TypeInfoResolver ?? throw new InvalidOperationException("MVC metadata resolver is required."));
    }
}

internal sealed class EmployeeAddressInputJsonTypeInfoResolver(IJsonTypeInfoResolver inner) : IJsonTypeInfoResolver
{
    private static readonly JsonConverter<int?> IdentityReader = new EmployeeAddressInt32InputConverter();
    private static readonly JsonConverter<DateTime?> AuditReader = new EmployeeAddressDateTimeInputConverter();

    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        var metadata = inner.GetTypeInfo(type, options);
        if (type != typeof(UpsertAddressRequest)) return metadata;
        if (metadata is null || metadata.Kind != JsonTypeInfoKind.Object)
            throw new InvalidOperationException("Native Address object metadata is required.");
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

internal sealed class EmployeeAddressInt32InputConverter : JsonConverter<int?>
{
    public override bool HandleNull => true;

    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => EmployeeAddressInputReader.ReadInt32(ref reader);

    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
        => throw new NotSupportedException("Virtual Address input metadata is never written.");
}

internal sealed class EmployeeAddressDateTimeInputConverter : JsonConverter<DateTime?>
{
    public override bool HandleNull => true;

    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => EmployeeAddressInputReader.ReadDateTime(ref reader);

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
        => throw new NotSupportedException("Virtual Address input metadata is never written.");
}

internal static class EmployeeAddressInputReader
{
    internal static int? ReadInt32(ref Utf8JsonReader input)
    {
        var scalar = Scalar(input);
        try
        {
            using var text = new StringReader(scalar);
            using var reader = Reader(text);
            return reader.ReadAsInt32();
        }
        catch (Exception exception) when (IsInputFailure(exception))
        {
            // The outer System.Text.Json traversal adds the real property path.
            // Never expose the public reader's input-bearing message or inner exception.
            throw new JsonException("Invalid Address identity metadata.");
        }
    }

    internal static DateTime? ReadDateTime(ref Utf8JsonReader input)
    {
        var scalar = Scalar(input);
        try
        {
            using var text = new StringReader(scalar);
            using var reader = Reader(text);
            return reader.ReadAsDateTime();
        }
        catch (Exception exception) when (IsInputFailure(exception))
        {
            throw new JsonException("Invalid Address audit metadata.");
        }
    }

    private static Newtonsoft.Json.JsonTextReader Reader(TextReader text) => new(text)
    {
        Culture = CultureInfo.InvariantCulture,
        DateParseHandling = Newtonsoft.Json.DateParseHandling.DateTime,
        DateTimeZoneHandling = Newtonsoft.Json.DateTimeZoneHandling.RoundtripKind,
    };

    private static string Scalar(Utf8JsonReader reader) => reader.TokenType switch
    {
        JsonTokenType.Null => "null",
        JsonTokenType.String => JsonSerializer.Serialize(reader.GetString()),
        JsonTokenType.True => "true",
        JsonTokenType.False => "false",
        JsonTokenType.Number => reader.HasValueSequence
            ? Encoding.UTF8.GetString(reader.ValueSequence.ToArray())
            : Encoding.UTF8.GetString(reader.ValueSpan),
        _ => throw new JsonException("Invalid Address input metadata token."),
    };

    private static bool IsInputFailure(Exception exception)
        => exception is Newtonsoft.Json.JsonException or ArgumentException or FormatException
            or InvalidCastException or OverflowException;
}

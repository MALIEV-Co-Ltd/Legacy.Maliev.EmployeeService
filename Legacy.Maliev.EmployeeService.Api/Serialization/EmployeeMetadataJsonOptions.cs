using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Legacy.Maliev.EmployeeService.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace Legacy.Maliev.EmployeeService.Api.Serialization;

/// <summary>Registers compatibility readers only for consumed employee metadata request strings.</summary>
public static class EmployeeMetadataJsonOptions
{
    /// <summary>Configures MVC request readers without changing minimal-API or response contracts.</summary>
    /// <param name="options">The existing MVC JSON options.</param>
    public static void Configure(JsonOptions options)
    {
        var converters = options.JsonSerializerOptions.Converters;
        if (!converters.Any(value => value is EmployeeConsumedStringRequestJsonConverter<UpsertAddressRequest>))
            converters.Add(new EmployeeConsumedStringRequestJsonConverter<UpsertAddressRequest>(
                ["Building", "AddressLine1", "AddressLine2", "City", "State", "PostalCode"]));
        if (!converters.Any(value => value is EmployeeConsumedStringRequestJsonConverter<UpsertSignatureImageFileRequest>))
            converters.Add(new EmployeeConsumedStringRequestJsonConverter<UpsertSignatureImageFileRequest>(
                ["Bucket", "ObjectName"]));
    }
}

internal sealed class EmployeeConsumedStringRequestJsonConverter<T>(string[] fieldNames) : JsonConverter<T>
    where T : class
{
    private readonly HashSet<string> fields = new(fieldNames, StringComparer.OrdinalIgnoreCase);

    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var native = NativeOptions(options);
        if (reader.TokenType != JsonTokenType.StartObject)
            return JsonSerializer.Deserialize<T>(ref reader, native);

        var original = reader;
        JsonDocument document;
        try
        {
            document = JsonDocument.ParseValue(ref reader);
        }
        catch (JsonException)
        {
            // Let the original DTO reader produce the malformed-input outcome/path.
            reader = original;
            return JsonSerializer.Deserialize<T>(ref reader, native);
        }

        using (document)
        {
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (fields.Contains(property.Name) && TryReadText(property.Value, out var text))
                    {
                        // Keep every non-null occurrence in order. A later null skips this
                        // consumed assignment only; IDs and unknown members stay native.
                        if (text is not null) writer.WriteString(property.Name, text);
                    }
                    else
                    {
                        // Unsupported string values must not disappear behind later good
                        // occurrences. Forward them and all other fields to the DTO reader.
                        writer.WritePropertyName(property.Name);
                        writer.WriteRawValue(property.Value.GetRawText(), skipInputValidation: true);
                    }
                }
                writer.WriteEndObject();
                writer.Flush();
            }

            return JsonSerializer.Deserialize<T>(
                buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length)), native);
        }
    }

    private static bool TryReadText(JsonElement value, out string? text)
    {
        text = null;
        switch (value.ValueKind)
        {
            case JsonValueKind.Null:
                return true;
            case JsonValueKind.String:
                text = value.GetString();
                return true;
            case JsonValueKind.True:
                text = "true";
                return true;
            case JsonValueKind.False:
                text = "false";
                return true;
            case JsonValueKind.Number:
                var lexeme = value.GetRawText();
                if (double.TryParse(lexeme, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    text = lexeme;
                    return true;
                }
                break;
        }
        return false;
    }

    private static JsonSerializerOptions NativeOptions(JsonSerializerOptions options)
    {
        var native = new JsonSerializerOptions(options);
        for (var index = native.Converters.Count - 1; index >= 0; index--)
            if (native.Converters[index] is EmployeeConsumedStringRequestJsonConverter<T>)
                native.Converters.RemoveAt(index);
        return native;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value, NativeOptions(options));
}

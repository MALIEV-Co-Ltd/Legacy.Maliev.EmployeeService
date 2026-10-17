using System.Globalization;
using Newtonsoft.Json;

namespace Legacy.Maliev.EmployeeService.Tests.Metadata;

// Public 13.0.3 reader reference only; not a historical resolved-binary oracle.
public static class EmployeeMetadataBindingCases
{
    public static readonly string[] AddressFields =
        ["Building", "AddressLine1", "AddressLine2", "City", "State", "PostalCode"];
    public static readonly string[] SignatureFields = ["Bucket", "ObjectName"];
    public static readonly IReadOnlyDictionary<string, (string Token, string? Expected)> Tokens =
        new Dictionary<string, (string, string?)>(StringComparer.Ordinal)
        {
            ["null"] = ("null", null),
            ["empty"] = ("\"\"", ""),
            ["padded"] = ("\" ก padded %_ \"", " ก padded %_ "),
            ["thai"] = ("\"ไทย\"", "ไทย"),
            ["escaped"] = ("\"line\\nquote\\\"\\\\end\"", "line\nquote\"\\end"),
            ["date_text"] = ("\"2026-01-02T03:04:05Z\"", "2026-01-02T03:04:05Z"),
            ["integer"] = ("-42", "-42"),
            ["decimal"] = ("12.50", "12.50"),
            ["exponent"] = ("1e2", "1e2"),
            ["negative_zero"] = ("-0", "-0"),
            ["capital_exponent"] = ("1E+03", "1E+03"),
            ["large_integer"] = ("9223372036854775808", "9223372036854775808"),
            ["large_exponent"] = ("1e400", "1e400"),
            ["true"] = ("true", "true"),
            ["false"] = ("false", "false"),
        };
    public static readonly IReadOnlyDictionary<string, string[]> Orders =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["replace"] = ["\"first\"", "\"last\""],
            ["null_after"] = ["\"first\"", "null"],
            ["null_before"] = ["null", "\"last\""],
            ["empty_after"] = ["\"first\"", "\"\""],
            ["repeat_null"] = ["\"first\"", "null", "null"],
            ["all_null"] = ["null", "null"],
            ["omitted"] = [],
        };
    public static readonly IReadOnlyDictionary<string, string> IdFragments =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["omitted"] = "",
            ["null"] = "null",
            ["zero"] = "0",
            ["negative"] = "-1",
            ["positive"] = "764",
            ["quoted"] = "\"764\"",
            ["quoted_spaces"] = "\" 764 \"",
            ["quoted_plus"] = "\"+764\"",
            ["empty"] = "\"\"",
            ["blank"] = "\" \"",
            ["fraction"] = "1.5",
            ["integral_float"] = "1.0",
            ["exponent"] = "1e2",
            ["overflow"] = "2147483648",
            ["true"] = "true",
            ["false"] = "false",
            ["object"] = "{}",
            ["array"] = "[]",
            ["minimum"] = "-2147483648",
        };

    public static IEnumerable<object[]> TokenCases => Tokens.Keys.Select(x => new object[] { x });
    public static IEnumerable<object[]> FieldTokenCases =>
        AllFields.SelectMany(fieldName => Tokens.Keys.Select(token => new object[] { fieldName, token }));
    public static IEnumerable<object[]> FieldOrderCases =>
        AllFields.SelectMany(fieldName => Orders.Keys.Select(order => new object[] { fieldName, order }));
    public static IEnumerable<object[]> MalformedCases =>
        AllFields.SelectMany(fieldName => new[] { "{}", "[]" }.SelectMany(token =>
            new[] { "only", "before", "after" }.Select(position => new object[] { fieldName, token, position })));
    public static IEnumerable<object[]> NativeIdCases =>
        new[] { "address", "signature" }.SelectMany(kind =>
            IdFragments.Keys.Select(id => new object[] { kind, id }));
    public static IEnumerable<object[]> RootCases =>
        new[] { "address", "signature" }.SelectMany(kind =>
            new[] { "null", "[]", "true", "42", "\"text\"", "{", "{} {}" }.Select(json => new object[] { kind, json }));
    public static IEnumerable<object[]> HttpFieldCases =>
        AddressFields.SelectMany(fieldName => new[] { "post", "put" }.Select(verb => new object[] { fieldName, verb }))
            .Concat(SignatureFields.Select(fieldName => new object[] { fieldName, "put" }));
    public static IEnumerable<object[]> HttpMalformedCases =>
        new[] { new object[] { "Building", "{}" }, new object[] { "PostalCode", "[]" },
            new object[] { "Bucket", "{}" }, new object[] { "ObjectName", "[]" } };
    public static IEnumerable<string> AllFields => AddressFields.Concat(SignatureFields);
    public static bool IsAddress(string field) => AddressFields.Contains(field, StringComparer.Ordinal);

    public static string Payload(string field, params string[] tokens)
    {
        var fields = IsAddress(field) ? AddressFields : SignatureFields;
        var parts = fields.Where(name => name != field)
            .Select(name => $"\"{name}\":\"unchanged\"").ToList();
        parts.Add(IsAddress(field) ? "\"CountryId\":764" : "\"EmployeeId\":1");
        parts.AddRange(tokens.Select((token, index) =>
            $"\"{(index % 2 == 0 ? field : field.ToLowerInvariant())}\":{token}"));
        return "{" + string.Join(",", parts) + "}";
    }

    public static string WithId(string kind, string fragment)
        => kind == "address"
            ? "{\"Building\":\"literal\"" + (fragment.Length == 0 ? "" : ",\"CountryId\":" + fragment) + "}"
            : "{\"Bucket\":\"literal\",\"ObjectName\":\"literal.png\"" + (fragment.Length == 0 ? "" : ",\"EmployeeId\":" + fragment) + "}";

    public static string Malformed(string field, string token, string position)
        => Payload(field, position == "only" ? [token]
            : position == "before" ? [token, "\"valid\""] : ["\"valid\"", token]);

    public static JsonTextReader Reader(string token)
        => new(new StringReader(token)) { Culture = CultureInfo.InvariantCulture, CloseInput = true };
    public static EmployeeMetadataPublicProjection PublicProjection(string json)
        => JsonConvert.DeserializeObject<EmployeeMetadataPublicProjection>(json,
            new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore, ReferenceLoopHandling = ReferenceLoopHandling.Ignore })!;
    public static string? Field(object value, string field)
        => (string?)value.GetType().GetProperty(field)!.GetValue(value);
}

public sealed class EmployeeMetadataPublicProjection
{
    public string? Building { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Bucket { get; set; }
    public string? ObjectName { get; set; }
}

using System.Globalization;
using Newtonsoft.Json;

namespace Legacy.Maliev.EmployeeService.Tests.Metadata;

// Public 13.0.3 reader reference only; not a historical resolved-binary oracle.
/// <summary>Provides literal metadata cases and public Newtonsoft 13.0.3 source-shaped projections, without historical binary or provider equivalence claims.</summary>
public static class EmployeeMetadataBindingCases
{
    /// <summary>Lists the six consumed address text properties exercised by metadata binding cases.</summary>
    public static readonly string[] AddressFields =
    /// <summary>Lists the Bucket and ObjectName signature text properties exercised by metadata binding cases.</summary>
        ["Building", "AddressLine1", "AddressLine2", "City", "State", "PostalCode"];
    public static readonly string[] SignatureFields = ["Bucket", "ObjectName"];
    /// <summary>Maps scalar case identifiers to raw JSON tokens and independently expected nullable text projections.</summary>
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
    /// <summary>Maps duplicate-order case identifiers to ordered raw JSON values, including omitted and repeated-null occurrences.</summary>
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
    /// <summary>Maps identifier case names to raw CountryId or EmployeeId fragments, including invalid scalar shapes.</summary>
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

    /// <summary>Provides one theory row for every scalar token case identifier.</summary>
    public static IEnumerable<object[]> TokenCases => Tokens.Keys.Select(x => new object[] { x });
    /// <summary>Combines every consumed address and signature text property with each scalar token case.</summary>
    public static IEnumerable<object[]> FieldTokenCases =>
        AllFields.SelectMany(fieldName => Tokens.Keys.Select(token => new object[] { fieldName, token }));
    /// <summary>Combines each consumed metadata text property with every duplicate-order scenario.</summary>
    public static IEnumerable<object[]> FieldOrderCases =>
        AllFields.SelectMany(fieldName => Orders.Keys.Select(order => new object[] { fieldName, order }));
    /// <summary>Combines metadata properties with invalid object or array values placed alone, before, or after text.</summary>
    public static IEnumerable<object[]> MalformedCases =>
        AllFields.SelectMany(fieldName => new[] { "{}", "[]" }.SelectMany(token =>
            new[] { "only", "before", "after" }.Select(position => new object[] { fieldName, token, position })));
    /// <summary>Combines address and signature request kinds with every native identifier representation case.</summary>
    public static IEnumerable<object[]> NativeIdCases =>
        new[] { "address", "signature" }.SelectMany(kind =>
            IdFragments.Keys.Select(id => new object[] { kind, id }));
    /// <summary>Combines address and signature request kinds with null, nonobject, incomplete, and multiple-document roots.</summary>
    public static IEnumerable<object[]> RootCases =>
        new[] { "address", "signature" }.SelectMany(kind =>
            new[] { "null", "[]", "true", "42", "\"text\"", "{", "{} {}" }.Select(json => new object[] { kind, json }));
    /// <summary>Provides address create/update and signature update lanes for consumed text binding through HTTP.</summary>
    public static IEnumerable<object[]> HttpFieldCases =>
        AddressFields.SelectMany(fieldName => new[] { "post", "put" }.Select(verb => new object[] { fieldName, verb }))
            .Concat(SignatureFields.Select(fieldName => new object[] { fieldName, "put" }));
    /// <summary>Provides representative malformed address and signature text values for HTTP rejection checks.</summary>
    public static IEnumerable<object[]> HttpMalformedCases =>
        new[] { new object[] { "Building", "{}" }, new object[] { "PostalCode", "[]" },
            new object[] { "Bucket", "{}" }, new object[] { "ObjectName", "[]" } };
    /// <summary>Enumerates all six address text properties followed by both signature text properties.</summary>
    public static IEnumerable<string> AllFields => AddressFields.Concat(SignatureFields);
    /// <summary>Determines whether a property belongs to the consumed address text field set.</summary>
    /// <param name="field">The case-sensitive metadata property name to compare with the address field list.</param>
    /// <returns>True for a listed address text property; otherwise, false.</returns>
    public static bool IsAddress(string field) => AddressFields.Contains(field, StringComparer.Ordinal);

    /// <summary>Builds a metadata body with ordered selected-property values and fixed unrelated text and identifier fields.</summary>
    /// <param name="field">The consumed Building, AddressLine1, AddressLine2, City, State, PostalCode, Bucket, or ObjectName property to exercise.</param>
    /// <param name="tokens">The raw JSON values emitted in order as duplicate selected-property occurrences, alternating original and lowercase spelling.</param>
    /// <returns>The constructed address or signature JSON object with unchanged peer fields and a fixed CountryId or EmployeeId.</returns>
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

    /// <summary>Builds an address or signature body with an optional raw identifier fragment.</summary>
    /// <param name="kind">The address request kind selecting CountryId, or signature selecting EmployeeId.</param>
    /// <param name="fragment">The raw identifier JSON value, or empty text to omit the identifier member.</param>
    /// <returns>The JSON body containing fixed text fields and the selected identifier fragment when supplied.</returns>
    public static string WithId(string kind, string fragment)
        => kind == "address"
            ? "{\"Building\":\"literal\"" + (fragment.Length == 0 ? "" : ",\"CountryId\":" + fragment) + "}"
            : "{\"Bucket\":\"literal\",\"ObjectName\":\"literal.png\"" + (fragment.Length == 0 ? "" : ",\"EmployeeId\":" + fragment) + "}";

    /// <summary>Builds a body placing an invalid metadata value alone or next to a valid duplicate.</summary>
    /// <param name="field">The consumed Building, AddressLine1, AddressLine2, City, State, PostalCode, Bucket, or ObjectName property to exercise.</param>
    /// <param name="token">The raw invalid object or array JSON value inserted for the selected text property.</param>
    /// <param name="position">The only, before, or after placement of the invalid occurrence relative to a valid text occurrence.</param>
    /// <returns>The metadata JSON body with the requested malformed occurrence ordering.</returns>
    public static string Malformed(string field, string token, string position)
        => Payload(field, position == "only" ? [token]
            : position == "before" ? [token, "\"valid\""] : ["\"valid\"", token]);

    /// <summary>Creates an invariant-culture public Newtonsoft reader that owns its backing text reader.</summary>
    /// <param name="token">The raw JSON text to expose through the new reader.</param>
    /// <returns>A JsonTextReader using invariant culture and configured to close its input when disposed.</returns>
    public static JsonTextReader Reader(string token)
        => new(new StringReader(token)) { Culture = CultureInfo.InvariantCulture, CloseInput = true };
    /// <summary>Projects metadata with the public Newtonsoft reader using ignored nulls and reference loops.</summary>
    /// <param name="json">The JSON body whose consumed metadata text values are projected by the public reader.</param>
    /// <returns>The public-reader metadata projection, or null if the supplied JSON represents no object.</returns>
    public static EmployeeMetadataPublicProjection PublicProjection(string json)
        => JsonConvert.DeserializeObject<EmployeeMetadataPublicProjection>(json,
            new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore, ReferenceLoopHandling = ReferenceLoopHandling.Ignore })!;
    /// <summary>Reads a named consumed text property from a bound metadata projection.</summary>
    /// <param name="value">The bound request or reference projection containing the selected public string property.</param>
    /// <param name="field">The exact public property name whose nullable text value is read through reflection.</param>
    /// <returns>The selected property's text value, or null when that property is unset.</returns>
    public static string? Field(object value, string field)
        => (string?)value.GetType().GetProperty(field)!.GetValue(value);
}

/// <summary>Carries consumed address and signature text for public-reader comparison without representing a historical resolved MVC provider.</summary>
public sealed class EmployeeMetadataPublicProjection
{
    /// <summary>The building text produced by the public metadata reader.</summary>
    public string? Building { get; set; }
    /// <summary>The first address-line text produced by the public metadata reader.</summary>
    public string? AddressLine1 { get; set; }
    /// <summary>The second address-line text produced by the public metadata reader.</summary>
    public string? AddressLine2 { get; set; }
    /// <summary>The city text produced by the public metadata reader.</summary>
    public string? City { get; set; }
    /// <summary>The state or province text produced by the public metadata reader.</summary>
    public string? State { get; set; }
    /// <summary>The postal-code text produced by the public metadata reader.</summary>
    public string? PostalCode { get; set; }
    /// <summary>The signature bucket text produced by the public metadata reader.</summary>
    public string? Bucket { get; set; }
    /// <summary>The signature object-name text produced by the public metadata reader.</summary>
    public string? ObjectName { get; set; }
}

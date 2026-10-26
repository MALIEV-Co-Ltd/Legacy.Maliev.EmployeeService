using System.Globalization;
using System.Text.Json;
using Legacy.Maliev.EmployeeService.Application.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

using MvcJsonOptions = Microsoft.AspNetCore.Mvc.JsonOptions;
using StjSerializer = System.Text.Json.JsonSerializer;

namespace Legacy.Maliev.EmployeeService.Tests.AddressInput;

// Public 13.0.3/source-shaped projection only; not the historical resolved MVC8 binary.
/// <summary>Defines address scalar cases and public Newtonsoft.Json 13 source-shaped comparators for request-binding tests.</summary>
public static class EmployeeAddressInputCases
{
    /// <summary>Describes a JSON scalar and its expected acceptance and converted value in the public source-shaped comparator.</summary>
    /// <param name="Json">The raw JSON scalar token submitted to the typed reader.</param>
    /// <param name="Accepted">Whether the public typed reader is expected to accept the token.</param>
    /// <param name="Integer">The expected nullable Int32 result for an integer case; null represents an empty or null value.</param>
    /// <param name="Kind">The expected DateTimeKind for an accepted date value; null represents no date value.</param>
    public sealed record ScalarCase(string Json, bool Accepted, int? Integer = null, DateTimeKind? Kind = null);
    /// <summary>Contains integer-token cases covering bounds, quoted numbers, nulls, and unsupported token shapes.</summary>
    public static readonly IReadOnlyDictionary<string, ScalarCase> Integers = new Dictionary<string, ScalarCase>(StringComparer.Ordinal)
    {
        ["null"] = new("null", true, null),
        ["zero"] = new("0", true, 0),
        ["positive"] = new("764", true, 764),
        ["negative"] = new("-1", true, -1),
        ["int_min"] = new("-2147483648", true, -2147483648),
        ["int_max"] = new("2147483647", true, 2147483647),
        ["underflow"] = new("-2147483649", false, null),
        ["overflow"] = new("2147483648", false, null),
        ["negative_zero"] = new("-0", true, 0),
        ["integral_fraction"] = new("1.0", false, null),
        ["fraction"] = new("1.5", false, null),
        ["exponent"] = new("1e2", false, null),
        ["fraction_exponent"] = new("1.5e1", false, null),
        ["large_integer"] = new("9223372036854775808", false, null),
        ["empty_text"] = new("\"\"", true, null),
        ["blank_text"] = new("\" \"", false, null),
        ["quoted_integer"] = new("\"42\"", true, 42),
        ["quoted_negative"] = new("\"-42\"", true, -42),
        ["quoted_padded"] = new("\" \\t42\\r\\n \"", true, 42),
        ["quoted_plus"] = new("\"+42\"", true, 42),
        ["quoted_leading_zero"] = new("\"001\"", true, 1),
        ["quoted_min"] = new("\"-2147483648\"", true, -2147483648),
        ["quoted_max"] = new("\"2147483647\"", true, 2147483647),
        ["quoted_overflow"] = new("\"2147483648\"", false, null),
        ["quoted_fraction"] = new("\"1.0\"", false, null),
        ["quoted_exponent"] = new("\"1e2\"", false, null),
        ["malformed_text"] = new("\"forty\"", false, null),
        ["grouped_text"] = new("\"1,234\"", false, null),
        ["literal_null_text"] = new("\"null\"", false, null),
        ["plus_only"] = new("\"+ \"", false, null),
        ["true"] = new("true", false, null),
        ["false"] = new("false", false, null),
        ["object"] = new("{}", false, null),
        ["array"] = new("[]", false, null),
    };
    /// <summary>Contains date-token cases covering ISO and Microsoft formats, offsets, calendar bounds, and unsupported shapes.</summary>
    public static readonly IReadOnlyDictionary<string, ScalarCase> Dates = new Dictionary<string, ScalarCase>(StringComparer.Ordinal)
    {
        ["null"] = new("null", true, Kind: null),
        ["empty_text"] = new("\"\"", true, Kind: null),
        ["blank_text"] = new("\" \"", false, Kind: null),
        ["date_only"] = new("\"2020-01-02\"", true, Kind: DateTimeKind.Unspecified),
        ["iso_utc"] = new("\"2020-01-02T03:04:05Z\"", true, Kind: DateTimeKind.Utc),
        ["iso_unzoned"] = new("\"2020-01-02T03:04:05\"", true, Kind: DateTimeKind.Unspecified),
        ["offset_plus"] = new("\"2020-01-02T03:04:05+07:00\"", true, Kind: DateTimeKind.Local),
        ["offset_minus"] = new("\"2020-01-02T03:04:05-05:30\"", true, Kind: DateTimeKind.Local),
        ["offset_99"] = new("\"2020-01-02T03:04:05+99:99\"", true, Kind: DateTimeKind.Local),
        ["offset_empty_minutes"] = new("\"2020-01-02T03:04:05+01:\"", true, Kind: DateTimeKind.Local),
        ["hour24"] = new("\"2020-01-02T24:00:00\"", true, Kind: DateTimeKind.Unspecified),
        ["hour24_zero_fraction"] = new("\"2020-01-02T24:00:00.000\"", true, Kind: DateTimeKind.Unspecified),
        ["hour24_nonzero_fraction"] = new("\"2020-01-02T24:00:00.001\"", false, Kind: null),
        ["invalid_minute"] = new("\"2020-01-02T03:60:05\"", false, Kind: null),
        ["leap_day"] = new("\"2020-02-29T03:04:05\"", true, Kind: DateTimeKind.Unspecified),
        ["invalid_leap"] = new("\"2019-02-29T03:04:05\"", false, Kind: null),
        ["invalid_month"] = new("\"2020-13-02T03:04:05\"", false, Kind: null),
        ["fraction7"] = new("\"2020-01-02T03:04:05.1234567Z\"", true, Kind: DateTimeKind.Utc),
        ["fraction8"] = new("\"2020-01-02T03:04:05.12345678Z\"", true, Kind: DateTimeKind.Utc),
        ["lower_z"] = new("\"2020-01-02T03:04:05z\"", true, Kind: DateTimeKind.Utc),
        ["minimum"] = new("\"0001-01-01T00:00:00\"", true, Kind: DateTimeKind.Unspecified),
        ["maximum"] = new("\"9999-12-31T23:59:59.9999999Z\"", true, Kind: DateTimeKind.Utc),
        ["maximum_hour24"] = new("\"9999-12-31T24:00:00\"", false, Kind: null),
        ["invariant_us"] = new("\"01/02/2020\"", true, Kind: DateTimeKind.Unspecified),
        ["invariant_invalid_day_first"] = new("\"31/12/2020\"", false, Kind: null),
        ["microsoft_epoch"] = new("\"/Date(0)/\"", true, Kind: DateTimeKind.Utc),
        ["microsoft_negative"] = new("\"/Date(-1)/\"", true, Kind: DateTimeKind.Utc),
        ["microsoft_offset"] = new("\"/Date(0+0700)/\"", true, Kind: DateTimeKind.Local),
        ["microsoft_hours"] = new("\"/Date(0+99)/\"", true, Kind: DateTimeKind.Local),
        ["microsoft_trailing_offset"] = new("\"/Date(0+0100trailing)/\"", true, Kind: DateTimeKind.Local),
        ["microsoft_maximum"] = new("\"/Date(253402300799999)/\"", true, Kind: DateTimeKind.Utc),
        ["microsoft_overflow"] = new("\"/Date(253402300800000)/\"", false, Kind: null),
        ["microsoft_wrapped_ticks"] = new("\"/Date(9223372036854775807)/\"", true, Kind: DateTimeKind.Utc),
        ["microsoft_invalid"] = new("\"/Date(nope)/\"", false, Kind: null),
        ["malformed_text"] = new("\"not-a-date\"", false, Kind: null),
        ["integer"] = new("0", false, Kind: null),
        ["negative_integer"] = new("-1", false, Kind: null),
        ["fraction"] = new("1.5", false, Kind: null),
        ["true"] = new("true", false, Kind: null),
        ["false"] = new("false", false, Kind: null),
        ["object"] = new("{}", false, Kind: null),
        ["array"] = new("[]", false, Kind: null),
    };
    /// <summary>Lists address identity and audit fields whose input values are validated without gaining write authority.</summary>
    public static readonly string[] Fields =
        ["Id", "CreatedDate", "ModifiedDate"];
    /// <summary>Lists repeated-property sequences used to test replacement, ignored nulls, case variants, and malformed occurrences.</summary>
    public static readonly string[] Orders =
        ["replace", "null_after", "null_before", "repeat_null", "empty_after", "all_null",
            "invalid_before_valid", "valid_before_invalid", "bad_then_null", "mixed_replace", "mixed_bad_before_valid"];
    /// <summary>Enumerates integer and date case identifiers for direct public typed-reader comparisons.</summary>
    public static IEnumerable<object[]> ReaderCases => new[] { "id", "date" }.SelectMany(kind =>
        (kind == "id" ? Integers : Dates).Keys.Select(caseId => new object[] { kind, caseId }));
    /// <summary>Enumerates each identity or audit field paired with its applicable scalar cases.</summary>
    public static IEnumerable<object[]> FieldCases => Fields.SelectMany(propertyName =>
        Cases(propertyName).Keys.Select(caseId => new object[] { propertyName, caseId }));
    /// <summary>Enumerates each identity or audit field paired with repeated-property order scenarios.</summary>
    public static IEnumerable<object[]> OrderCases => Fields.SelectMany(propertyName =>
        Orders.Select(orderId => new object[] { propertyName, orderId }));
    /// <summary>Enumerates POST and PUT address requests with selected identity and audit scalar cases.</summary>
    public static IEnumerable<object[]> HttpCases => new[] { "post", "put" }.SelectMany(verb =>
        new[] { "Id", "CreatedDate", "ModifiedDate" }.SelectMany(propertyName =>
            HttpTokens(propertyName).Select(caseId => new object[] { verb, propertyName, caseId })));
    /// <summary>Enumerates POST and PUT address requests with selected repeated-property order scenarios.</summary>
    public static IEnumerable<object[]> HttpOrders => new[] { "post", "put" }.SelectMany(verb =>
        new[] { "Id", "CreatedDate", "ModifiedDate" }.SelectMany(propertyName =>
            new[] { "null_after", "invalid_before_valid", "valid_before_invalid", "mixed_replace" }
                .Select(orderId => new object[] { verb, propertyName, orderId })));
    /// <summary>Selects representative scalar cases for HTTP testing of an address identity or audit field.</summary>
    /// <param name="propertyName">The identity or audit property selecting integer cases for Id and date cases otherwise.</param>
    /// <returns>The case identifiers selected for HTTP coverage of the requested field.</returns>
    public static string[] HttpTokens(string propertyName) => IsId(propertyName)
        ? ["null", "quoted_padded", "int_min", "int_max", "empty_text", "blank_text", "fraction", "exponent", "overflow", "object"]
        : ["null", "empty_text", "blank_text", "iso_utc", "offset_plus", "hour24", "microsoft_epoch",
            "microsoft_offset", "invalid_leap", "integer", "true", "object"];
    /// <summary>Determines whether a property name denotes the address identity field without regard to casing.</summary>
    /// <param name="propertyName">The JSON property name compared with Id using ordinal case-insensitive matching.</param>
    /// <returns>True when the property name denotes Id; otherwise, false.</returns>
    public static bool IsId(string propertyName) => propertyName.Equals("Id", StringComparison.OrdinalIgnoreCase);
    /// <summary>Selects the integer or date scalar corpus for an address identity or audit property.</summary>
    /// <param name="propertyName">The property selecting integer cases for Id and date cases for audit timestamps.</param>
    /// <returns>The integer corpus for Id or the date corpus for an audit field.</returns>
    public static IReadOnlyDictionary<string, ScalarCase> Cases(string propertyName) => IsId(propertyName) ? Integers : Dates;
    /// <summary>Builds an address request with fixed writable fields and one supplied identity or audit token.</summary>
    /// <param name="propertyName">The identity or audit JSON member to append to the address payload.</param>
    /// <param name="token">The raw JSON token inserted as the selected member value.</param>
    /// <returns>An address JSON object containing literal Thai building text, country 764, and the supplied member.</returns>
    public static string Payload(string propertyName, string token)
        => "{\"Building\":\"literal ไทย\",\"CountryId\":764,\"" + propertyName + "\":" + token + "}";
    /// <summary>Builds repeated identity or audit members in the requested occurrence order and casing pattern.</summary>
    /// <param name="propertyName">The identity or audit member repeated in the generated address object.</param>
    /// <param name="orderId">The case identifier controlling valid replacements, ignored nulls, empty text, errors, or mixed casing.</param>
    /// <returns>The JSON address object with ordered occurrences for the selected property.</returns>
    public static string Ordered(string propertyName, string orderId)
    {
        var valid = IsId(propertyName) ? "42" : "\"2020-01-02T03:04:05Z\"";
        var second = IsId(propertyName) ? "7" : "\"2021-02-03T04:05:06Z\"";
        string[] values = orderId switch
        {
            "replace" or "mixed_replace" => [valid, second],
            "null_after" => [valid, "null"],
            "null_before" => ["null", valid],
            "repeat_null" => [valid, "null", "null"],
            "empty_after" => [valid, "\"\""],
            "all_null" => ["null", "null"],
            "invalid_before_valid" or "mixed_bad_before_valid" => ["{}", valid],
            "valid_before_invalid" => [valid, "[]"],
            _ => ["{}", "null"],
        };
        var members = values.Select((value, index) =>
            "\"" + (orderId.StartsWith("mixed_", StringComparison.Ordinal) && index > 0
                ? propertyName.ToUpperInvariant() : propertyName) + "\":" + value);
        return "{\"Building\":\"literal ไทย\",\"CountryId\":764," + string.Join(",", members) + "}";
    }
    /// <summary>Identifies repeated-property cases that contain no unsupported scalar occurrence.</summary>
    /// <param name="orderId">The repeated-property scenario identifier checked for malformed occurrences.</param>
    /// <returns>False for sequences containing an invalid occurrence; true for the accepted sequences.</returns>
    public static bool OrderAccepted(string orderId) => orderId is not
        ("invalid_before_valid" or "mixed_bad_before_valid" or "valid_before_invalid" or "bad_then_null");
    /// <summary>Creates the invariant public Newtonsoft.Json 13 settings used by the source-shaped address comparator.</summary>
    /// <returns>New serializer settings that ignore null assignments and reference loops and preserve round-trip date kinds.</returns>
    public static JsonSerializerSettings SourceSettings() => new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        Culture = CultureInfo.InvariantCulture,
        DateParseHandling = DateParseHandling.DateTime,
        DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind,
    };
    /// <summary>Deserializes an address into the mutable source-shaped comparator using public Newtonsoft.Json 13.</summary>
    /// <param name="json">The JSON address payload passed to the public source-shaped deserializer.</param>
    /// <returns>The deserialized mutable address projection used for comparison.</returns>
    public static SourceAddress Source(string json)
        => JsonConvert.DeserializeObject<SourceAddress>(json, SourceSettings())!;
    /// <summary>Reads an integer or date scalar through the public Newtonsoft.Json typed reader.</summary>
    /// <param name="kind">The reader selector: id invokes ReadAsInt32; other cases invoke ReadAsDateTime.</param>
    /// <param name="token">The raw JSON scalar token consumed by the selected typed reader.</param>
    /// <returns>The converted nullable integer or date value; null for a null or accepted empty token.</returns>
    public static object? ReadScalar(string kind, string token)
    {
        using var input = new StringReader(token);
        using var reader = new JsonTextReader(input)
        {
            Culture = CultureInfo.InvariantCulture,
            DateParseHandling = DateParseHandling.DateTime,
            DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind,
        };
        if (kind == "id") return reader.ReadAsInt32();
        return reader.ReadAsDateTime();
    }
    /// <summary>Extracts identity or audit values from a deserialized source-shaped address.</summary>
    /// <param name="value">The mutable address projection produced by the public comparator.</param>
    /// <param name="propertyName">The member selecting Id, CreatedDate, or ModifiedDate with case-insensitive matching.</param>
    /// <returns>The selected identifier or nullable audit timestamp.</returns>
    public static object? SourceValue(SourceAddress value, string propertyName)
    {
        if (IsId(propertyName)) return value.Id;
        return propertyName.Equals("CreatedDate", StringComparison.OrdinalIgnoreCase) ? value.CreatedDate : value.ModifiedDate;
    }
    /// <summary>Binds an address payload with the supplied current System.Text.Json request options.</summary>
    /// <param name="json">The complete JSON address request to bind.</param>
    /// <param name="options">The current or native MVC serializer settings selected by the test.</param>
    /// <returns>The bound address request, or null when the JSON root is null.</returns>
    public static UpsertAddressRequest? Bind(string json, JsonSerializerOptions options)
        => StjSerializer.Deserialize<UpsertAddressRequest>(json, options);

    /// <summary>Represents the mutable address shape consumed by the public Newtonsoft.Json 13 comparator.</summary>
    public sealed class SourceAddress
    {
        /// <summary>Stores the comparator address identifier whose scalar conversion is checked independently of write authority.</summary>
        public int Id { get; set; }
        /// <summary>Stores the building text retained by the source-shaped address projection.</summary>
        public string? Building { get; set; }
        /// <summary>Stores the first address line consumed by the source-shaped projection.</summary>
        public string? AddressLine1 { get; set; }
        /// <summary>Stores the optional second address line consumed by the source-shaped projection.</summary>
        public string? AddressLine2 { get; set; }
        /// <summary>Stores the locality text consumed by the source-shaped address projection.</summary>
        public string? City { get; set; }
        /// <summary>Stores the state or province text consumed by the source-shaped address projection.</summary>
        public string? State { get; set; }
        /// <summary>Stores the postal text consumed by the source-shaped address projection.</summary>
        public string? PostalCode { get; set; }
        /// <summary>Stores the numeric country reference retained alongside tested identity and audit inputs.</summary>
        public int CountryId { get; set; }
        /// <summary>Stores the nullable creation timestamp and date kind read by the public comparator.</summary>
        public DateTime? CreatedDate { get; set; }
        /// <summary>Stores the nullable modification timestamp and date kind read by the public comparator.</summary>
        public DateTime? ModifiedDate { get; set; }
    }
}

internal static class EmployeeAddressInputNative
{
    private const string ConfigurationName = "Legacy.Maliev.EmployeeService.Api.Serialization.EmployeeAddressInputJsonOptions";
    internal static bool IsOwned(IConfigureOptions<MvcJsonOptions> configuration)
        => configuration is ConfigureNamedOptions<MvcJsonOptions> { Name: "", Action: { } action }
            && action.Target is null
            && action.GetInvocationList().Length == 1
            && action.Method.Name == "Configure"
            && action.Method.DeclaringType?.FullName == ConfigurationName
            && action.Method.DeclaringType?.Assembly == typeof(Program).Assembly;

    internal static JsonSerializerOptions Create(IServiceProvider services)
    {
        var source = services.GetServices<IConfigureOptions<MvcJsonOptions>>().ToArray();
        var owned = source.Where(IsOwned).ToArray();
        var expectedOwned = typeof(Program).Assembly.GetType(ConfigurationName, throwOnError: false) is null ? 0 : 1;
        Assert.Equal(expectedOwned, owned.Length); // Only the untouched b347 API lacks this public helper type.
        var retained = source.Where(configuration => !IsOwned(configuration)).ToArray();
        Assert.Equal(source.Length - owned.Length, retained.Length);
        var index = 0;
        foreach (var configuration in source)
        {
            if (IsOwned(configuration)) continue;
            Assert.Same(configuration, retained[index++]);
        }
        var native = new OptionsFactory<MvcJsonOptions>(retained,
            services.GetServices<IPostConfigureOptions<MvcJsonOptions>>(),
            services.GetServices<IValidateOptions<MvcJsonOptions>>())
            .Create(Microsoft.Extensions.Options.Options.DefaultName).JsonSerializerOptions;
        Assert.DoesNotContain(native.GetTypeInfo(typeof(UpsertAddressRequest)).Properties,
            property => property.Name is "Id" or "CreatedDate" or "ModifiedDate");
        Assert.Equal(new[] { "AddressLine1", "AddressLine2", "Building", "City", "PostalCode", "State" },
            native.GetTypeInfo(typeof(UpsertAddressRequest)).Properties.Where(property =>
                property.CustomConverter?.GetType().FullName ==
                "Legacy.Maliev.EmployeeService.Api.Serialization.EmployeeConsumedMetadataStringJsonConverter")
                .Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        return native;
    }
}

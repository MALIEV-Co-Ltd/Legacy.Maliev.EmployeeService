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
public static class EmployeeAddressInputCases
{
    public sealed record ScalarCase(string Json, bool Accepted, int? Integer = null, DateTimeKind? Kind = null);
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
    public static readonly string[] Fields =
        ["Id", "CreatedDate", "ModifiedDate"];
    public static readonly string[] Orders =
        ["replace", "null_after", "null_before", "repeat_null", "empty_after", "all_null",
            "invalid_before_valid", "valid_before_invalid", "bad_then_null", "mixed_replace", "mixed_bad_before_valid"];
    public static IEnumerable<object[]> ReaderCases => new[] { "id", "date" }.SelectMany(kind =>
        (kind == "id" ? Integers : Dates).Keys.Select(caseId => new object[] { kind, caseId }));
    public static IEnumerable<object[]> FieldCases => Fields.SelectMany(propertyName =>
        Cases(propertyName).Keys.Select(caseId => new object[] { propertyName, caseId }));
    public static IEnumerable<object[]> OrderCases => Fields.SelectMany(propertyName =>
        Orders.Select(orderId => new object[] { propertyName, orderId }));
    public static IEnumerable<object[]> HttpCases => new[] { "post", "put" }.SelectMany(verb =>
        new[] { "Id", "CreatedDate", "ModifiedDate" }.SelectMany(propertyName =>
            HttpTokens(propertyName).Select(caseId => new object[] { verb, propertyName, caseId })));
    public static IEnumerable<object[]> HttpOrders => new[] { "post", "put" }.SelectMany(verb =>
        new[] { "Id", "CreatedDate", "ModifiedDate" }.SelectMany(propertyName =>
            new[] { "null_after", "invalid_before_valid", "valid_before_invalid", "mixed_replace" }
                .Select(orderId => new object[] { verb, propertyName, orderId })));
    public static string[] HttpTokens(string propertyName) => IsId(propertyName)
        ? ["null", "quoted_padded", "int_min", "int_max", "empty_text", "blank_text", "fraction", "exponent", "overflow", "object"]
        : ["null", "empty_text", "blank_text", "iso_utc", "offset_plus", "hour24", "microsoft_epoch",
            "microsoft_offset", "invalid_leap", "integer", "true", "object"];
    public static bool IsId(string propertyName) => propertyName.Equals("Id", StringComparison.OrdinalIgnoreCase);
    public static IReadOnlyDictionary<string, ScalarCase> Cases(string propertyName) => IsId(propertyName) ? Integers : Dates;
    public static string Payload(string propertyName, string token)
        => "{\"Building\":\"literal ไทย\",\"CountryId\":764,\"" + propertyName + "\":" + token + "}";
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
    public static bool OrderAccepted(string orderId) => orderId is not
        ("invalid_before_valid" or "mixed_bad_before_valid" or "valid_before_invalid" or "bad_then_null");
    public static JsonSerializerSettings SourceSettings() => new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        Culture = CultureInfo.InvariantCulture,
        DateParseHandling = DateParseHandling.DateTime,
        DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind,
    };
    public static SourceAddress Source(string json)
        => JsonConvert.DeserializeObject<SourceAddress>(json, SourceSettings())!;
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
    public static object? SourceValue(SourceAddress value, string propertyName)
    {
        if (IsId(propertyName)) return value.Id;
        return propertyName.Equals("CreatedDate", StringComparison.OrdinalIgnoreCase) ? value.CreatedDate : value.ModifiedDate;
    }
    public static UpsertAddressRequest? Bind(string json, JsonSerializerOptions options)
        => StjSerializer.Deserialize<UpsertAddressRequest>(json, options);

    public sealed class SourceAddress
    {
        public int Id { get; set; }
        public string? Building { get; set; }
        public string? AddressLine1 { get; set; }
        public string? AddressLine2 { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? PostalCode { get; set; }
        public int CountryId { get; set; }
        public DateTime? CreatedDate { get; set; }
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

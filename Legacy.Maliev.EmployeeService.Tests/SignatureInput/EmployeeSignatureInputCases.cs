using System.Text.Json;
using Legacy.Maliev.EmployeeService.Application.Models;
using Legacy.Maliev.EmployeeService.Tests.AddressInput;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

using MvcJsonOptions = Microsoft.AspNetCore.Mvc.JsonOptions;
using StjSerializer = System.Text.Json.JsonSerializer;

namespace Legacy.Maliev.EmployeeService.Tests.SignatureInput;

// Public13/source-shaped projection only; no historical resolved MVC8 binary claim.
/// <summary>Provides signature metadata scalar and duplicate-order cases with a public Newtonsoft source-shaped comparator.</summary>
public static class EmployeeSignatureInputCases
{
    /// <summary>The identifier and timestamp property names Id, CreatedDate, and ModifiedDate used for signature input characterization.</summary>
    public static readonly string[] Fields = EmployeeAddressInputCases.Fields;
    /// <summary>The duplicate-assignment order scenarios shared with address input characterization.</summary>
    public static readonly string[] Orders = EmployeeAddressInputCases.Orders;
    /// <summary>Theory rows pairing each signature identifier or timestamp property with its scalar corpus case keys.</summary>
    public static IEnumerable<object[]> FieldCases => EmployeeAddressInputCases.FieldCases;
    /// <summary>Theory rows pairing each signature identifier or timestamp property with every duplicate-assignment order.</summary>
    public static IEnumerable<object[]> OrderCases => EmployeeAddressInputCases.OrderCases;
    /// <summary>Bounded HTTP scalar cases pairing signature identifier or timestamp fields with selected corpus keys.</summary>
    public static IEnumerable<object[]> HttpCases => Fields.SelectMany(propertyName =>
        EmployeeAddressInputCases.HttpTokens(propertyName).Select(caseId => new object[] { propertyName, caseId }));
    /// <summary>Bounded HTTP duplicate-order cases covering later nulls, invalid occurrences, and mixed-case replacement.</summary>
    public static IEnumerable<object[]> HttpOrders => Fields.SelectMany(propertyName =>
        new[] { "null_after", "invalid_before_valid", "valid_before_invalid", "mixed_replace" }
            .Select(orderId => new object[] { propertyName, orderId }));
    /// <summary>Classifies the identifier property using an ordinal case-insensitive comparison.</summary>
    /// <param name="propertyName">The property name to compare with Id.</param>
    /// <returns>True when the property name equals Id ignoring case; otherwise, false.</returns>
    public static bool IsId(string propertyName) => EmployeeAddressInputCases.IsId(propertyName);
    /// <summary>Selects the integer or date scalar corpus for a signature characterization property.</summary>
    /// <param name="propertyName">The signature characterization property: Id for integer cases, or CreatedDate or ModifiedDate for date cases.</param>
    /// <returns>The keyed integer corpus for Id, or the keyed date corpus for timestamp properties.</returns>
    public static IReadOnlyDictionary<string, EmployeeAddressInputCases.ScalarCase> Cases(string propertyName)
        => EmployeeAddressInputCases.Cases(propertyName);
    /// <summary>Reports whether the duplicate-assignment scenario contains no unsupported consumed occurrence.</summary>
    /// <param name="orderId">The duplicate-order case key identifying replacement, null ordering, or an invalid occurrence.</param>
    /// <returns>False for scenarios containing an invalid occurrence; otherwise, true.</returns>
    public static bool OrderAccepted(string orderId) => EmployeeAddressInputCases.OrderAccepted(orderId);
    /// <summary>Builds signature JSON with fixed literal bucket, object name, and owner values plus the selected scalar token.</summary>
    /// <param name="propertyName">The signature characterization property: Id for integer cases, or CreatedDate or ModifiedDate for date cases.</param>
    /// <param name="token">The raw JSON scalar or container token assigned to the selected characterization property.</param>
    /// <returns>A signature JSON object containing the fixed metadata and selected property occurrence.</returns>
    public static string Payload(string propertyName, string token)
        => SignaturePayload(EmployeeAddressInputCases.Payload(propertyName, token));
    /// <summary>Builds signature JSON with ordered repeated occurrences of the selected identifier or timestamp property.</summary>
    /// <param name="propertyName">The signature characterization property: Id for integer cases, or CreatedDate or ModifiedDate for date cases.</param>
    /// <param name="orderId">The duplicate-order scenario selecting values, nulls, invalid containers, and optional mixed property casing.</param>
    /// <returns>A signature JSON object containing fixed literal metadata and the ordered repeated property occurrences.</returns>
    public static string Ordered(string propertyName, string orderId)
        => SignaturePayload(EmployeeAddressInputCases.Ordered(propertyName, orderId));
    private static string SignaturePayload(string json)
        => json.Replace("\"Building\":\"literal ไทย\",\"CountryId\":764",
            "\"Bucket\":\"literal ไทย\",\"ObjectName\":\"literal ไทย.png\",\"EmployeeId\":7", StringComparison.Ordinal);
    /// <summary>Reads a mutable source-shaped signature using the public Newtonsoft serializer and shared invariant null-ignore settings.</summary>
    /// <param name="json">The signature JSON body to project through the public source-shaped comparator.</param>
    /// <returns>The deserialized source-shaped signature; a whole JSON null can yield null despite the nonnullable annotation.</returns>
    public static SourceSignature Source(string json)
        => JsonConvert.DeserializeObject<SourceSignature>(json, EmployeeAddressInputCases.SourceSettings())!;
    /// <summary>Reads the signature request DTO using the supplied System.Text.Json options.</summary>
    /// <param name="json">The signature JSON body to deserialize into the request DTO.</param>
    /// <param name="options">The candidate or native serializer options controlling signature request deserialization.</param>
    /// <returns>The deserialized signature request, or null when the JSON body represents null.</returns>
    public static UpsertSignatureImageFileRequest? Bind(string json, JsonSerializerOptions options)
        => StjSerializer.Deserialize<UpsertSignatureImageFileRequest>(json, options);

    /// <summary>Represents the mutable signature shape used only by the public Newtonsoft comparison reader.</summary>
    public sealed class SourceSignature
    {
        /// <summary>The source-shaped signature row identifier inspected for scalar coercion even though the consumer discards it.</summary>
        public int Id { get; set; }
        /// <summary>The scalar employee owner identifier projected by the source-shaped signature reader.</summary>
        public int EmployeeId { get; set; }
        /// <summary>The literal bucket metadata projected by the source-shaped signature reader.</summary>
        public string? Bucket { get; set; }
        /// <summary>The literal storage object-name metadata projected by the source-shaped signature reader.</summary>
        public string? ObjectName { get; set; }
        /// <summary>The nullable creation timestamp inspected by the source-shaped reader even though the consumer discards it.</summary>
        public DateTime? CreatedDate { get; set; }
        /// <summary>The nullable modification timestamp inspected by the source-shaped reader even though the consumer discards it.</summary>
        public DateTime? ModifiedDate { get; set; }
    }
}

internal static class EmployeeSignatureInputNative
{
    private const string ConfigurationName = "Legacy.Maliev.EmployeeService.Api.Serialization.EmployeeSignatureInputJsonOptions";
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
        Assert.Equal(expectedOwned, owned.Length); // Only the untouched fa4 API lacks this public helper type.
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
        Assert.DoesNotContain(native.GetTypeInfo(typeof(UpsertSignatureImageFileRequest)).Properties,
            property => property.Name is "Id" or "CreatedDate" or "ModifiedDate");
        Assert.Equal(new[] { "Bucket", "ObjectName" },
            native.GetTypeInfo(typeof(UpsertSignatureImageFileRequest)).Properties.Where(property =>
                property.CustomConverter?.GetType().FullName ==
                "Legacy.Maliev.EmployeeService.Api.Serialization.EmployeeConsumedMetadataStringJsonConverter")
                .Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        if (typeof(Program).Assembly.GetType("Legacy.Maliev.EmployeeService.Api.Serialization.EmployeeAddressInputJsonOptions", throwOnError: false) is not null)
        {
            Assert.All(new[] { "Id", "CreatedDate", "ModifiedDate" }, name =>
                Assert.Contains(native.GetTypeInfo(typeof(UpsertAddressRequest)).Properties, property => property.Name == name));
        }
        return native;
    }
}

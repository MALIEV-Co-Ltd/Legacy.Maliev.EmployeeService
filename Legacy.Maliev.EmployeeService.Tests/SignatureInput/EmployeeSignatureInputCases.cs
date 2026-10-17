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
public static class EmployeeSignatureInputCases
{
    public static readonly string[] Fields = EmployeeAddressInputCases.Fields;
    public static readonly string[] Orders = EmployeeAddressInputCases.Orders;
    public static IEnumerable<object[]> FieldCases => EmployeeAddressInputCases.FieldCases;
    public static IEnumerable<object[]> OrderCases => EmployeeAddressInputCases.OrderCases;
    public static IEnumerable<object[]> HttpCases => Fields.SelectMany(propertyName =>
        EmployeeAddressInputCases.HttpTokens(propertyName).Select(caseId => new object[] { propertyName, caseId }));
    public static IEnumerable<object[]> HttpOrders => Fields.SelectMany(propertyName =>
        new[] { "null_after", "invalid_before_valid", "valid_before_invalid", "mixed_replace" }
            .Select(orderId => new object[] { propertyName, orderId }));
    public static bool IsId(string propertyName) => EmployeeAddressInputCases.IsId(propertyName);
    public static IReadOnlyDictionary<string, EmployeeAddressInputCases.ScalarCase> Cases(string propertyName)
        => EmployeeAddressInputCases.Cases(propertyName);
    public static bool OrderAccepted(string orderId) => EmployeeAddressInputCases.OrderAccepted(orderId);
    public static string Payload(string propertyName, string token)
        => SignaturePayload(EmployeeAddressInputCases.Payload(propertyName, token));
    public static string Ordered(string propertyName, string orderId)
        => SignaturePayload(EmployeeAddressInputCases.Ordered(propertyName, orderId));
    private static string SignaturePayload(string json)
        => json.Replace("\"Building\":\"literal ไทย\",\"CountryId\":764",
            "\"Bucket\":\"literal ไทย\",\"ObjectName\":\"literal ไทย.png\",\"EmployeeId\":7", StringComparison.Ordinal);
    public static SourceSignature Source(string json)
        => JsonConvert.DeserializeObject<SourceSignature>(json, EmployeeAddressInputCases.SourceSettings())!;
    public static UpsertSignatureImageFileRequest? Bind(string json, JsonSerializerOptions options)
        => StjSerializer.Deserialize<UpsertSignatureImageFileRequest>(json, options);

    public sealed class SourceSignature
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string? Bucket { get; set; }
        public string? ObjectName { get; set; }
        public DateTime? CreatedDate { get; set; }
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

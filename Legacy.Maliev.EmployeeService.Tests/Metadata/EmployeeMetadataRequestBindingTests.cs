using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Legacy.Maliev.EmployeeService.Application.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.EmployeeService.Tests.Metadata;

// Compiles on baseline: production converter types are not referenced by these tests.
[Collection("Employee scaffold runtime")]
public sealed class EmployeeMetadataRequestBindingTests(EmployeeRouteAcceptanceFixture fixture)
    : IClassFixture<EmployeeRouteAcceptanceFixture>
{
    private const string PropertyReaderName = "Legacy.Maliev.EmployeeService.Api.Serialization.EmployeeConsumedMetadataStringJsonConverter";
    private JsonSerializerOptions Options => fixture.Factory.Services
        .GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>().Value.JsonSerializerOptions;

    [Theory]
    [MemberData(nameof(EmployeeMetadataBindingCases.FieldTokenCases), MemberType = typeof(EmployeeMetadataBindingCases))]
    public void ConsumedField_MatchesPublicTypedReader(string field, string tokenId)
    {
        var token = EmployeeMetadataBindingCases.Tokens[tokenId].Token;
        using var reader = EmployeeMetadataBindingCases.Reader(token);
        var actual = Bind(Kind(field), EmployeeMetadataBindingCases.Payload(field, token), Options);
        Assert.NotNull(actual);
        Assert.Equal(reader.ReadAsString(), EmployeeMetadataBindingCases.Field(actual, field));
    }

    [Theory]
    [MemberData(nameof(EmployeeMetadataBindingCases.FieldOrderCases), MemberType = typeof(EmployeeMetadataBindingCases))]
    public void OccurrenceOrder_MatchesNullIgnoringProjection(string field, string orderId)
    {
        var json = EmployeeMetadataBindingCases.Payload(field, EmployeeMetadataBindingCases.Orders[orderId]);
        var expected = EmployeeMetadataBindingCases.PublicProjection(json);
        var actual = Bind(Kind(field), json, Options);
        Assert.NotNull(actual);
        Assert.Equal(EmployeeMetadataBindingCases.Field(expected, field), EmployeeMetadataBindingCases.Field(actual, field));
    }

    [Theory]
    [MemberData(nameof(EmployeeMetadataBindingCases.MalformedCases), MemberType = typeof(EmployeeMetadataBindingCases))]
    public void UnsupportedOccurrence_KeepsNativeFailurePath(string field, string token, string position)
    {
        var json = EmployeeMetadataBindingCases.Malformed(field, token, position);
        Assert.ThrowsAny<Newtonsoft.Json.JsonException>(() => EmployeeMetadataBindingCases.PublicProjection(json));
        var baseline = Assert.Throws<JsonException>(() => Bind(Kind(field), json, NativeOptions()));
        var actual = Assert.Throws<JsonException>(() => Bind(Kind(field), json, Options));
        Assert.Equal(baseline.Path, actual.Path);
    }

    [Theory]
    [MemberData(nameof(EmployeeMetadataBindingCases.NativeIdCases), MemberType = typeof(EmployeeMetadataBindingCases))]
    public void NonStringIdPolicy_DelegatesToExistingMvcReader(string kind, string idCase)
        => AssertNativeEquivalent(kind, EmployeeMetadataBindingCases.WithId(kind, EmployeeMetadataBindingCases.IdFragments[idCase]));

    [Theory]
    [InlineData("address_bad_then_valid")]
    [InlineData("address_valid_then_null")]
    [InlineData("signature_bad_then_valid")]
    [InlineData("signature_valid_then_null")]
    public void RepeatedIdPolicy_CannotBeReplacedByStringNullPolicy(string caseId)
    {
        var (kind, json) = caseId switch
        {
            "address_bad_then_valid" => ("address", "{\"Building\":\"literal\",\"CountryId\":{},\"CountryId\":764}"),
            "address_valid_then_null" => ("address", "{\"Building\":\"literal\",\"CountryId\":764,\"countryid\":null}"),
            "signature_bad_then_valid" => ("signature", "{\"Bucket\":\"literal\",\"ObjectName\":\"literal\",\"EmployeeId\":{},\"EmployeeId\":1}"),
            _ => ("signature", "{\"Bucket\":\"literal\",\"ObjectName\":\"literal\",\"EmployeeId\":1,\"employeeid\":null}"),
        };
        AssertNativeEquivalent(kind, json);
    }

    [Theory]
    [MemberData(nameof(EmployeeMetadataBindingCases.RootCases), MemberType = typeof(EmployeeMetadataBindingCases))]
    public void RootAndMalformedJson_KeepNativeOutcome(string kind, string json) => AssertNativeEquivalent(kind, json);

    [Theory]
    [InlineData("address_id")]
    [InlineData("signature_id")]
    [InlineData("address_unknown")]
    [InlineData("signature_text")]
    public void MalformedSyntax_ReplaysNativeFieldFailure(string caseId)
    {
        var (kind, json, expectedPath) = caseId switch
        {
            "address_id" => ("address", "{\"Building\":\"literal\",\"CountryId\":", "$.CountryId"),
            "signature_id" => ("signature", "{\"Bucket\":\"literal\",\"ObjectName\":\"literal\",\"EmployeeId\":", "$.EmployeeId"),
            "address_unknown" => ("address", "{\"Building\":\"literal\",\"Unknown\":{\"deep\":[1,", "$.Unknown"),
            _ => ("signature", "{\"Bucket\":\"unfinished", "$.Bucket"),
        };
        AssertNativeEquivalent(kind, json, expectedPath);
    }

    [Fact]
    public void Registration_IsMvcRequestSpecificAndWriterUnchanged()
    {
        Assert.Single(fixture.Factory.Services.GetServices<IConfigureOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>(),
            EmployeeMetadataNativeOptions.IsOwnedConfiguration);
        Assert.DoesNotContain(Options.Converters, converter => converter.CanConvert(typeof(string))
            || converter.CanConvert(typeof(UpsertAddressRequest))
            || converter.CanConvert(typeof(UpsertSignatureImageFileRequest)));
        AssertScopedProperties(typeof(UpsertAddressRequest), EmployeeMetadataBindingCases.AddressFields);
        AssertScopedProperties(typeof(UpsertSignatureImageFileRequest), EmployeeMetadataBindingCases.SignatureFields);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<string>("123", Options));
        const string addressPrimitive = "{\"Building\":123,\"CountryId\":764}";
        var nativeAddressError = Assert.Throws<JsonException>(() => Bind("address", addressPrimitive, NativeOptions()));
        Assert.Equal("$.Building", nativeAddressError.Path);
        Assert.Equal("123", Assert.IsType<UpsertAddressRequest>(Bind("address", addressPrimitive, Options)).Building);
        const string signaturePrimitive = "{\"Bucket\":true,\"ObjectName\":\"literal\"}";
        var nativeSignatureError = Assert.Throws<JsonException>(() => Bind("signature", signaturePrimitive, NativeOptions()));
        Assert.Equal("$.Bucket", nativeSignatureError.Path);
        Assert.Equal("true", Assert.IsType<UpsertSignatureImageFileRequest>(Bind("signature", signaturePrimitive, Options)).Bucket);
        foreach (var type in new[] { typeof(UpsertRoleRequest), typeof(EmployeeHomeAddressEditRequest), typeof(AddressResponse) })
            Assert.DoesNotContain(Options.GetTypeInfo(type).Properties, property =>
                property.CustomConverter?.GetType().FullName == PropertyReaderName);
        AssertNativeEquivalent("address", "{}");
        AssertNativeEquivalent("signature", "{}");
        AssertNativeEquivalent("address", "null");
        AssertNativeEquivalent("signature", "null");
        var omittedAddress = Assert.IsType<UpsertAddressRequest>(Bind("address", "{}", Options));
        Assert.Equal(0, omittedAddress.CountryId);
        var omittedSignature = Assert.IsType<UpsertSignatureImageFileRequest>(Bind("signature", "{}", Options));
        Assert.Null(omittedSignature.Bucket);
        Assert.Null(omittedSignature.ObjectName);
        Assert.Null(omittedSignature.EmployeeId);
        Assert.Equal(JsonSerializer.Serialize<UpsertAddressRequest?>(null, NativeOptions()),
            JsonSerializer.Serialize<UpsertAddressRequest?>(null, Options));
        Assert.Equal(JsonSerializer.Serialize<UpsertSignatureImageFileRequest?>(null, NativeOptions()),
            JsonSerializer.Serialize<UpsertSignatureImageFileRequest?>(null, Options));
        var minimal = fixture.Factory.Services.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UpsertAddressRequest>("{\"Building\":123}", minimal));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<EmployeeHomeAddressEditRequest>("{\"AddressId\":1,\"Building\":123}", Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<EmployeeHomeAddressEditRequest>("{\"AddressId\":1,\"Unknown\":true}", Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AddressResponse>("{\"Building\":123}", Options));
        var address = new UpsertAddressRequest(null, " literal ไทย ", null, null, null, null, 764);
        var signature = new UpsertSignatureImageFileRequest(" literal ", " literal ไทย.png ", null);
        Assert.Equal(JsonSerializer.Serialize(address, NativeOptions()), JsonSerializer.Serialize(address, Options));
        Assert.Equal(JsonSerializer.Serialize(signature, NativeOptions()), JsonSerializer.Serialize(signature, Options));
    }

    [Theory]
    [InlineData("address")]
    [InlineData("signature")]
    public void IgnoredMembers_KeepCurrentDtoPolicyWithoutClaimingSourceGraphParity(string kind)
    {
        var json = kind == "address"
            ? "{\"Building\":\"literal\",\"CountryId\":764,\"Employee\":[{}],\"Unknown\":{}}"
            : "{\"Bucket\":\"literal\",\"ObjectName\":\"literal\",\"Id\":{},\"CreatedDate\":[],\"ModifiedDate\":true,\"Unknown\":{\"Bucket\":[]}}";
        AssertNativeEquivalent(kind, json);
        if (kind == "address")
        {
            foreach (var name in new[] { "Id", "CreatedDate", "ModifiedDate" })
            {
                var invalid = "{\"Building\":\"literal\",\"CountryId\":764,\"" + name + "\":{}}";
                Assert.NotNull(Bind(kind, invalid, NativeOptions()));
                var error = Assert.Throws<JsonException>(() => Bind(kind, invalid, Options));
                Assert.Equal("$." + name, error.Path);
            }
        }
    }

    private void AssertNativeEquivalent(string kind, string json, string? expectedPath = null)
    {
        object? expected = null;
        object? actual = null;
        var baselineError = Record.Exception(() => { expected = Bind(kind, json, NativeOptions()); });
        var actualError = Record.Exception(() => { actual = Bind(kind, json, Options); });
        if (baselineError is null)
        {
            Assert.Null(actualError);
            Assert.Equal(expected, actual);
        }
        else
        {
            Assert.NotNull(actualError);
            Assert.Equal(baselineError.GetType(), actualError.GetType());
            var nativePath = Assert.IsType<JsonException>(baselineError).Path;
            Assert.Equal(nativePath, Assert.IsType<JsonException>(actualError).Path);
            if (expectedPath is not null) Assert.Equal(expectedPath, nativePath);
        }
    }

    private JsonSerializerOptions NativeOptions()
    {
        var resolver = Options.TypeInfoResolver;
        var chain = Options.TypeInfoResolverChain.ToArray();
        var native = EmployeeMetadataNativeOptions.Create(fixture.Factory.Services);
        EmployeeMetadataNativeOptions.AssertNativeScope(native);
        Assert.NotSame(Options, native);
        Assert.Same(resolver, Options.TypeInfoResolver);
        Assert.Equal(chain.Length, Options.TypeInfoResolverChain.Count);
        for (var index = 0; index < chain.Length; index++) Assert.Same(chain[index], Options.TypeInfoResolverChain[index]);
        return native;
    }
    private void AssertScopedProperties(Type type, string[] names)
    {
        var metadata = Options.GetTypeInfo(type);
        var create = metadata.CreateObject;
        Assert.NotNull(create);
        var owned = metadata.Properties.Where(property => property.CustomConverter?.GetType().FullName == PropertyReaderName).ToArray();
        Assert.Equal(names.Order(StringComparer.Ordinal).ToArray(), owned.Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.All(owned, property =>
        {
            var set = property.Set;
            Assert.NotNull(set);
            var target = create();
            set(target, "first");
            set(target, null);
            Assert.Equal("first", EmployeeMetadataBindingCases.Field(target, property.Name));
            set(target, "");
            Assert.Equal("", EmployeeMetadataBindingCases.Field(target, property.Name));
        });
        Assert.DoesNotContain(metadata.Properties.Where(property => !names.Contains(property.Name, StringComparer.Ordinal)),
            property => property.CustomConverter?.GetType().FullName == PropertyReaderName);
    }
    private static string Kind(string field) => EmployeeMetadataBindingCases.IsAddress(field) ? "address" : "signature";
    private static object? Bind(string kind, string json, JsonSerializerOptions options)
        => kind == "address" ? JsonSerializer.Deserialize<UpsertAddressRequest>(json, options)
            : JsonSerializer.Deserialize<UpsertSignatureImageFileRequest>(json, options);
}

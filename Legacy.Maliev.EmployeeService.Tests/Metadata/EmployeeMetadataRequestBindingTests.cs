using System.Text.Json;
using Legacy.Maliev.EmployeeService.Application.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.EmployeeService.Tests.Metadata;

// Compiles on baseline: production converter types are not referenced by these tests.
[Collection("Employee scaffold runtime")]
public sealed class EmployeeMetadataRequestBindingTests(EmployeeRouteAcceptanceFixture fixture)
    : IClassFixture<EmployeeRouteAcceptanceFixture>
{
    private const string ConverterPrefix = "Legacy.Maliev.EmployeeService.Api.Serialization.EmployeeConsumedStringRequestJsonConverter";
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
        var (kind, json) = caseId switch
        {
            "address_id" => ("address", "{\"Building\":\"literal\",\"CountryId\":"),
            "signature_id" => ("signature", "{\"Bucket\":\"literal\",\"ObjectName\":\"literal\",\"EmployeeId\":"),
            "address_unknown" => ("address", "{\"Building\":\"literal\",\"Unknown\":{\"deep\":[1,"),
            _ => ("signature", "{\"Bucket\":\"unfinished"),
        };
        AssertNativeEquivalent(kind, json);
    }

    [Fact]
    public void Registration_IsMvcRequestSpecificAndWriterUnchanged()
    {
        Assert.Single(Options.Converters, converter => IsOwned(converter) && converter.CanConvert(typeof(UpsertAddressRequest)));
        Assert.Single(Options.Converters, converter => IsOwned(converter) && converter.CanConvert(typeof(UpsertSignatureImageFileRequest)));
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
            ? "{\"Building\":\"literal\",\"CountryId\":764,\"Id\":{},\"CreatedDate\":[],\"ModifiedDate\":true,\"Employee\":[{}]}"
            : "{\"Bucket\":\"literal\",\"ObjectName\":\"literal\",\"Id\":{},\"CreatedDate\":[],\"ModifiedDate\":true,\"Unknown\":{\"Bucket\":[]}}";
        AssertNativeEquivalent(kind, json);
    }

    private void AssertNativeEquivalent(string kind, string json)
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
            Assert.Equal(Assert.IsType<JsonException>(baselineError).Path, Assert.IsType<JsonException>(actualError).Path);
        }
    }

    private JsonSerializerOptions NativeOptions()
    {
        var native = new JsonSerializerOptions(Options);
        for (var index = native.Converters.Count - 1; index >= 0; index--)
            if (IsOwned(native.Converters[index])) native.Converters.RemoveAt(index);
        return native;
    }
    private static bool IsOwned(System.Text.Json.Serialization.JsonConverter converter)
        => converter.GetType().FullName?.StartsWith(ConverterPrefix, StringComparison.Ordinal) == true;
    private static string Kind(string field) => EmployeeMetadataBindingCases.IsAddress(field) ? "address" : "signature";
    private static object? Bind(string kind, string json, JsonSerializerOptions options)
        => kind == "address" ? JsonSerializer.Deserialize<UpsertAddressRequest>(json, options)
            : JsonSerializer.Deserialize<UpsertSignatureImageFileRequest>(json, options);
}

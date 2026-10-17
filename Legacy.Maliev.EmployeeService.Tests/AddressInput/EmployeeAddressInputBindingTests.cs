using System.Text.Json;
using Legacy.Maliev.EmployeeService.Application.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.EmployeeService.Tests.AddressInput;

[Collection("Employee scaffold runtime")]
public sealed class EmployeeAddressInputBindingTests(EmployeeRouteAcceptanceFixture fixture)
    : IClassFixture<EmployeeRouteAcceptanceFixture>
{
    private JsonSerializerOptions Options => fixture.Factory.Services
        .GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>().Value.JsonSerializerOptions;

    [Theory]
    [MemberData(nameof(EmployeeAddressInputCases.FieldCases), MemberType = typeof(EmployeeAddressInputCases))]
    public void CandidateMatchesSource(string propertyName, string caseId)
    {
        var planned = EmployeeAddressInputCases.Cases(propertyName)[caseId];
        var json = EmployeeAddressInputCases.Payload(propertyName, planned.Json);
        var sourceError = Record.Exception(() => EmployeeAddressInputCases.Source(json));
        Assert.Equal(planned.Accepted, sourceError is null);
        if (!planned.Accepted)
        {
            var error = Assert.Throws<JsonException>(() => EmployeeAddressInputCases.Bind(json, Options));
            Assert.Equal("$." + propertyName, error.Path);
        }
        else
        {
            var actual = EmployeeAddressInputCases.Bind(json, Options);
            Assert.NotNull(actual);
            Assert.Equal("literal ไทย", actual.Building);
            Assert.Equal(764, actual.CountryId);
        }
    }

    [Theory]
    [MemberData(nameof(EmployeeAddressInputCases.OrderCases), MemberType = typeof(EmployeeAddressInputCases))]
    public void CandidateOrdersMatchSource(string propertyName, string orderId)
    {
        var json = EmployeeAddressInputCases.Ordered(propertyName, orderId);
        var sourceError = Record.Exception(() => EmployeeAddressInputCases.Source(json));
        Assert.Equal(EmployeeAddressInputCases.OrderAccepted(orderId), sourceError is null);
        if (sourceError is not null)
        {
            var error = Assert.Throws<JsonException>(() => EmployeeAddressInputCases.Bind(json, Options));
            Assert.Equal("$." + propertyName, error.Path);
        }
        else
        {
            var actual = EmployeeAddressInputCases.Bind(json, Options);
            Assert.NotNull(actual);
            Assert.Equal("literal ไทย", actual.Building);
            Assert.Equal(764, actual.CountryId);
        }
    }

    [Theory]
    [MemberData(nameof(EmployeeAddressInputCases.FieldCases), MemberType = typeof(EmployeeAddressInputCases))]
    public void CurrentNativeBaseline_IgnoresIgnoredMetadata(string propertyName, string caseId)
    {
        var top = Options.TypeInfoResolver;
        var chain = Options.TypeInfoResolverChain.ToArray();
        var native = EmployeeAddressInputNative.Create(fixture.Factory.Services);
        Assert.NotSame(Options, native);
        var actual = EmployeeAddressInputCases.Bind(EmployeeAddressInputCases.Payload(propertyName,
            EmployeeAddressInputCases.Cases(propertyName)[caseId].Json), native);
        Assert.NotNull(actual);
        Assert.Equal("literal ไทย", actual.Building);
        Assert.Equal(764, actual.CountryId);
        Assert.Same(top, Options.TypeInfoResolver);
        Assert.Equal(chain.Length, Options.TypeInfoResolverChain.Count);
        for (var index = 0; index < chain.Length; index++) Assert.Same(chain[index], Options.TypeInfoResolverChain[index]);
    }

    [Theory]
    [InlineData("Id", "{\"Building\":\"literal\",\"Id\":")]
    [InlineData("Id", "{\"Id\":\"unfinished")]
    [InlineData("CreatedDate", "{\"CreatedDate\":\"unfinished")]
    [InlineData("ModifiedDate", "{\"ModifiedDate\":{\"deep\":[1,")]
    public void MalformedSyntax_KeepsCurrentNativePath(string propertyName, string json)
    {
        var native = EmployeeAddressInputNative.Create(fixture.Factory.Services);
        var expected = Assert.Throws<JsonException>(() => EmployeeAddressInputCases.Bind(json, native));
        var actual = Assert.Throws<JsonException>(() => EmployeeAddressInputCases.Bind(json, Options));
        Assert.Equal("$." + propertyName, expected.Path);
        Assert.Equal(expected.Path, actual.Path);
    }

    [Theory]
    [InlineData("id", "quoted_padded")]
    [InlineData("ID", "object")]
    [InlineData("createddate", "offset_plus")]
    [InlineData("CREATEDDATE", "invalid_leap")]
    [InlineData("modifieddate", "microsoft_epoch")]
    [InlineData("MODIFIEDDATE", "array")]
    public void PropertySpelling_RemainsCaseInsensitive(string propertyName, string caseId)
        => CandidateMatchesSource(propertyName, caseId);

    [Fact]
    public void RegistrationScopeAndWriterRemainNative()
    {
        Assert.Single(fixture.Factory.Services.GetServices<IConfigureOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>(),
            EmployeeAddressInputNative.IsOwned);
        foreach (var name in new[] { "Id", "CreatedDate", "ModifiedDate" })
            Assert.Null(typeof(UpsertAddressRequest).GetProperty(name));
        const string prefix = "Legacy.Maliev.EmployeeService.Api.Serialization.EmployeeAddress";
        var metadata = Options.GetTypeInfo(typeof(UpsertAddressRequest));
        var virtualProperties = metadata.Properties.Where(property =>
            property.CustomConverter?.GetType().FullName?.StartsWith(prefix, StringComparison.Ordinal) == true).ToArray();
        Assert.Equal(new[] { "CreatedDate", "Id", "ModifiedDate" }, virtualProperties.Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.All(virtualProperties, property =>
        {
            Assert.Null(property.Get);
            var setter = property.Set;
            var shouldSerialize = property.ShouldSerialize;
            Assert.NotNull(setter);
            Assert.False(property.IsRequired);
            Assert.NotNull(shouldSerialize);
            var original = new UpsertAddressRequest("literal", null, null, null, null, null, 764);
            setter(original, null);
            Assert.Equal(new UpsertAddressRequest("literal", null, null, null, null, null, 764), original);
            Assert.False(shouldSerialize(original, null));
        });
        var native = EmployeeAddressInputNative.Create(fixture.Factory.Services);
        var sample = new UpsertAddressRequest("literal ไทย", null, " ", null, null, "", 764);
        Assert.Equal(JsonSerializer.Serialize(sample, native), JsonSerializer.Serialize(sample, Options));
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(sample, Options));
        foreach (var name in new[] { "Id", "CreatedDate", "ModifiedDate" })
            Assert.False(wire.RootElement.TryGetProperty(name, out _));
        Assert.Null(EmployeeAddressInputCases.Bind("null", Options));
        Assert.DoesNotContain(Options.Converters, converter =>
            converter.GetType().FullName?.StartsWith(prefix, StringComparison.Ordinal) == true);
        foreach (var type in new[] { typeof(UpsertRoleRequest), typeof(UpsertSignatureImageFileRequest), typeof(AddressResponse) })
            Assert.DoesNotContain(Options.GetTypeInfo(type).Properties, property =>
                property.CustomConverter?.GetType().FullName?.StartsWith(prefix, StringComparison.Ordinal) == true);
        var minimal = fixture.Factory.Services
            .GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
        Assert.DoesNotContain(minimal.GetTypeInfo(typeof(UpsertAddressRequest)).Properties,
            property => property.Name is "Id" or "CreatedDate" or "ModifiedDate");
        var unknown = EmployeeAddressInputCases.Bind(
            "{\"Building\":\"literal\",\"CountryId\":764,\"Unknown\":{},\"Employee\":[{}]}", Options);
        Assert.NotNull(unknown);
    }
}

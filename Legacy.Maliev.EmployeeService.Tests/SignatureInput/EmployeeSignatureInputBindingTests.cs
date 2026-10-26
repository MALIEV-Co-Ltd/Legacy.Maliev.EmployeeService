using System.Text.Json;
using Legacy.Maliev.EmployeeService.Application.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.EmployeeService.Tests.SignatureInput;

/// <summary>Checks signature identity and audit input validation against the public source-shaped comparator and current native baseline.</summary>
/// <param name="fixture">The shared production Employee host, PostgreSQL, Redis, and controlled authorization fixture used by these assertions.</param>
[Collection("Employee scaffold runtime")]
public sealed class EmployeeSignatureInputBindingTests(EmployeeRouteAcceptanceFixture fixture)
    : IClassFixture<EmployeeRouteAcceptanceFixture>
{
    private JsonSerializerOptions Options => fixture.Factory.Services
        .GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>().Value.JsonSerializerOptions;

    /// <summary>Checks signature identity and audit scalars against public Newtonsoft.Json 13 acceptance without granting input write authority.</summary>
    /// <param name="propertyName">The signature identity or audit JSON member whose scalar is validated.</param>
    /// <param name="caseId">The scalar corpus identifier selecting the token and expected public-comparator acceptance.</param>
    [Theory]
    [MemberData(nameof(EmployeeSignatureInputCases.FieldCases), MemberType = typeof(EmployeeSignatureInputCases))]
    public void CandidateMatchesSource(string propertyName, string caseId)
    {
        var planned = EmployeeSignatureInputCases.Cases(propertyName)[caseId];
        var json = EmployeeSignatureInputCases.Payload(propertyName, planned.Json);
        var sourceError = Record.Exception(() => EmployeeSignatureInputCases.Source(json));
        Assert.Equal(planned.Accepted, sourceError is null);
        if (!planned.Accepted)
        {
            var error = Assert.Throws<JsonException>(() => EmployeeSignatureInputCases.Bind(json, Options));
            Assert.Equal("$." + propertyName, error.Path);
        }
        else
        {
            var actual = EmployeeSignatureInputCases.Bind(json, Options);
            Assert.NotNull(actual);
            Assert.Equal("literal ไทย", actual.Bucket);
            Assert.Equal(7, actual.EmployeeId);
        }
    }

    /// <summary>Checks repeated signature identity or audit occurrences preserve comparator acceptance and earlier malformed failures.</summary>
    /// <param name="propertyName">The identity or audit member repeated in the signature payload.</param>
    /// <param name="orderId">The sequence identifier controlling valid replacements, nulls, errors, or mixed member casing.</param>
    [Theory]
    [MemberData(nameof(EmployeeSignatureInputCases.OrderCases), MemberType = typeof(EmployeeSignatureInputCases))]
    public void CandidateOrdersMatchSource(string propertyName, string orderId)
    {
        var json = EmployeeSignatureInputCases.Ordered(propertyName, orderId);
        var sourceError = Record.Exception(() => EmployeeSignatureInputCases.Source(json));
        Assert.Equal(EmployeeSignatureInputCases.OrderAccepted(orderId), sourceError is null);
        if (sourceError is not null)
        {
            var error = Assert.Throws<JsonException>(() => EmployeeSignatureInputCases.Bind(json, Options));
            Assert.Equal("$." + propertyName, error.Path);
        }
        else
        {
            var actual = EmployeeSignatureInputCases.Bind(json, Options);
            Assert.NotNull(actual);
            Assert.Equal("literal ไทย", actual.Bucket);
            Assert.Equal(7, actual.EmployeeId);
        }
    }

    /// <summary>Checks that the current native baseline ignores identity and audit members while leaving shared serializer metadata unchanged.</summary>
    /// <param name="propertyName">The ignored signature identity or audit member inserted into the payload.</param>
    /// <param name="caseId">The scalar case identifier whose token must remain ignored by the native baseline.</param>
    [Theory]
    [MemberData(nameof(EmployeeSignatureInputCases.FieldCases), MemberType = typeof(EmployeeSignatureInputCases))]
    public void CurrentNativeBaseline_IgnoresIgnoredMetadata(string propertyName, string caseId)
    {
        var top = Options.TypeInfoResolver;
        var chain = Options.TypeInfoResolverChain.ToArray();
        var native = EmployeeSignatureInputNative.Create(fixture.Factory.Services);
        Assert.NotSame(Options, native);
        var actual = EmployeeSignatureInputCases.Bind(EmployeeSignatureInputCases.Payload(propertyName,
            EmployeeSignatureInputCases.Cases(propertyName)[caseId].Json), native);
        Assert.NotNull(actual);
        Assert.Equal("literal ไทย", actual.Bucket);
        Assert.Equal(7, actual.EmployeeId);
        Assert.Same(top, Options.TypeInfoResolver);
        Assert.Equal(chain.Length, Options.TypeInfoResolverChain.Count);
        for (var index = 0; index < chain.Length; index++) Assert.Same(chain[index], Options.TypeInfoResolverChain[index]);
    }

    /// <summary>Checks that malformed signature JSON retains the current native failing member path.</summary>
    /// <param name="propertyName">The member expected in the JSON error path.</param>
    /// <param name="json">The malformed or unfinished signature JSON payload sent to both binders.</param>
    [Theory]
    [InlineData("Id", "{\"Bucket\":\"literal\",\"Id\":")]
    [InlineData("Id", "{\"Id\":\"unfinished")]
    [InlineData("CreatedDate", "{\"CreatedDate\":\"unfinished")]
    [InlineData("ModifiedDate", "{\"ModifiedDate\":{\"deep\":[1,")]
    public void MalformedSyntax_KeepsCurrentNativePath(string propertyName, string json)
    {
        var native = EmployeeSignatureInputNative.Create(fixture.Factory.Services);
        var expected = Assert.Throws<JsonException>(() => EmployeeSignatureInputCases.Bind(json, native));
        var actual = Assert.Throws<JsonException>(() => EmployeeSignatureInputCases.Bind(json, Options));
        Assert.Equal("$." + propertyName, expected.Path);
        Assert.Equal(expected.Path, actual.Path);
    }

    /// <summary>Checks that signature identity and audit member names remain case-insensitive.</summary>
    /// <param name="propertyName">The lowercase or uppercase spelling of the signature identity or audit member.</param>
    /// <param name="caseId">The scalar corpus case used with the alternate property spelling.</param>
    [Theory]
    [InlineData("id", "quoted_padded")]
    [InlineData("ID", "object")]
    [InlineData("createddate", "offset_plus")]
    [InlineData("CREATEDDATE", "invalid_leap")]
    [InlineData("modifieddate", "microsoft_epoch")]
    [InlineData("MODIFIEDDATE", "array")]
    public void PropertySpelling_RemainsCaseInsensitive(string propertyName, string caseId)
        => CandidateMatchesSource(propertyName, caseId);

    /// <summary>Checks that signature validation metadata is request-scoped and leaves native writers and unrelated DTOs unchanged.</summary>
    [Fact]
    public void RegistrationScopeAndWriterRemainNative()
    {
        Assert.Single(fixture.Factory.Services.GetServices<IConfigureOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>(),
            EmployeeSignatureInputNative.IsOwned);
        foreach (var name in new[] { "Id", "CreatedDate", "ModifiedDate" })
            Assert.Null(typeof(UpsertSignatureImageFileRequest).GetProperty(name));
        const string prefix = "Legacy.Maliev.EmployeeService.Api.Serialization.EmployeeSignature";
        var metadata = Options.GetTypeInfo(typeof(UpsertSignatureImageFileRequest));
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
            var original = new UpsertSignatureImageFileRequest("literal", "literal ไทย.png", 7);
            setter(original, null);
            Assert.Equal(new UpsertSignatureImageFileRequest("literal", "literal ไทย.png", 7), original);
            Assert.False(shouldSerialize(original, null));
        });
        var native = EmployeeSignatureInputNative.Create(fixture.Factory.Services);
        var sample = new UpsertSignatureImageFileRequest("literal ไทย", "literal ไทย.png", 7);
        Assert.Equal(JsonSerializer.Serialize(sample, native), JsonSerializer.Serialize(sample, Options));
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(sample, Options));
        foreach (var name in new[] { "Id", "CreatedDate", "ModifiedDate" })
            Assert.False(wire.RootElement.TryGetProperty(name, out _));
        Assert.Null(EmployeeSignatureInputCases.Bind("null", Options));
        Assert.DoesNotContain(Options.Converters, converter =>
            converter.GetType().FullName?.StartsWith(prefix, StringComparison.Ordinal) == true);
        foreach (var type in new[] { typeof(UpsertRoleRequest), typeof(UpsertAddressRequest), typeof(SignatureImageFileResponse) })
            Assert.DoesNotContain(Options.GetTypeInfo(type).Properties, property =>
                property.CustomConverter?.GetType().FullName?.StartsWith(prefix, StringComparison.Ordinal) == true);
        var minimal = fixture.Factory.Services
            .GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
        Assert.DoesNotContain(minimal.GetTypeInfo(typeof(UpsertSignatureImageFileRequest)).Properties,
            property => property.Name is "Id" or "CreatedDate" or "ModifiedDate");
        var unknown = EmployeeSignatureInputCases.Bind(
            "{\"Bucket\":\"literal\",\"EmployeeId\":7,\"Unknown\":{},\"Employee\":[{}]}", Options);
        Assert.NotNull(unknown);
    }
}

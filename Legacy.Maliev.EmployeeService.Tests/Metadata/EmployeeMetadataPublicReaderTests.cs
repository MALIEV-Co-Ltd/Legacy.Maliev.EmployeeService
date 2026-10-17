using Newtonsoft.Json;

namespace Legacy.Maliev.EmployeeService.Tests.Metadata;

public sealed class EmployeeMetadataPublicReaderTests
{
    [Theory]
    [MemberData(nameof(EmployeeMetadataBindingCases.TokenCases), MemberType = typeof(EmployeeMetadataBindingCases))]
    public void PublicTypedReader_PreservesReferenceLexeme(string tokenId)
    {
        var (token, expected) = EmployeeMetadataBindingCases.Tokens[tokenId];
        using var reader = EmployeeMetadataBindingCases.Reader(token);
        Assert.Equal(expected, reader.ReadAsString());
    }

    [Theory]
    [MemberData(nameof(EmployeeMetadataBindingCases.FieldOrderCases), MemberType = typeof(EmployeeMetadataBindingCases))]
    public void PublicMutableProjection_IgnoresNullInOccurrenceOrder(string field, string orderId)
    {
        var tokens = EmployeeMetadataBindingCases.Orders[orderId];
        var expected = orderId switch
        {
            "replace" or "null_before" => "last",
            "null_after" or "repeat_null" => "first",
            "empty_after" => "",
            _ => null,
        };
        Assert.Equal(expected, EmployeeMetadataBindingCases.Field(
            EmployeeMetadataBindingCases.PublicProjection(EmployeeMetadataBindingCases.Payload(field, tokens)), field));
    }

    [Theory]
    [MemberData(nameof(EmployeeMetadataBindingCases.MalformedCases), MemberType = typeof(EmployeeMetadataBindingCases))]
    public void PublicMutableProjection_CannotHideUnsupportedOccurrence(string field, string token, string position)
        => Assert.ThrowsAny<JsonException>(() =>
            EmployeeMetadataBindingCases.PublicProjection(EmployeeMetadataBindingCases.Malformed(field, token, position)));
}

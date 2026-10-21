using Newtonsoft.Json;

namespace Legacy.Maliev.EmployeeService.Tests.Metadata;

/// <summary>Checks public Newtonsoft.Json 13 typed-string and mutable source-shaped metadata comparisons.</summary>
public sealed class EmployeeMetadataPublicReaderTests
{
    /// <summary>Checks that the public typed string reader preserves each reference token lexeme.</summary>
    /// <param name="tokenId">The scalar case identifier selecting a raw JSON token and its expected string lexeme.</param>
    [Theory]
    [MemberData(nameof(EmployeeMetadataBindingCases.TokenCases), MemberType = typeof(EmployeeMetadataBindingCases))]
    public void PublicTypedReader_PreservesReferenceLexeme(string tokenId)
    {
        var (token, expected) = EmployeeMetadataBindingCases.Tokens[tokenId];
        using var reader = EmployeeMetadataBindingCases.Reader(token);
        Assert.Equal(expected, reader.ReadAsString());
    }

    /// <summary>Checks null-ignoring assignment order in a mutable metadata projection.</summary>
    /// <param name="field">The consumed address or signature string field repeated in the comparator payload.</param>
    /// <param name="orderId">The occurrence sequence selecting replacement, ignored nulls, empty text, or all-null values.</param>
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

    /// <summary>Checks that an unsupported metadata occurrence cannot be hidden by later values in the public mutable comparator.</summary>
    /// <param name="field">The consumed metadata string member receiving the unsupported occurrence.</param>
    /// <param name="token">The unsupported raw JSON token inserted into the repeated-member payload.</param>
    /// <param name="position">The placement selector controlling where the unsupported occurrence appears among valid or null values.</param>
    [Theory]
    [MemberData(nameof(EmployeeMetadataBindingCases.MalformedCases), MemberType = typeof(EmployeeMetadataBindingCases))]
    public void PublicMutableProjection_CannotHideUnsupportedOccurrence(string field, string token, string position)
        => Assert.ThrowsAny<JsonException>(() =>
            EmployeeMetadataBindingCases.PublicProjection(EmployeeMetadataBindingCases.Malformed(field, token, position)));
}

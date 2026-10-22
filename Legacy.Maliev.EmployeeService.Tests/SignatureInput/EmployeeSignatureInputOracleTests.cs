using Legacy.Maliev.EmployeeService.Tests.AddressInput;

namespace Legacy.Maliev.EmployeeService.Tests.SignatureInput;

/// <summary>Checks public Newtonsoft.Json 13 source-shaped signature comparisons against the address scalar reader corpus.</summary>
public sealed class EmployeeSignatureInputOracleTests
{
    /// <summary>Checks source-shaped signature identity and date values agree with the public typed-reader address comparator.</summary>
    /// <param name="propertyName">The signature identity or audit field whose converted value and date kind are compared.</param>
    /// <param name="caseId">The scalar case selecting the raw token and expected comparator acceptance.</param>
    [Theory]
    [MemberData(nameof(EmployeeSignatureInputCases.FieldCases), MemberType = typeof(EmployeeSignatureInputCases))]
    public void MutableSource_UsesPublicTypedReader(string propertyName, string caseId)
    {
        var planned = EmployeeSignatureInputCases.Cases(propertyName)[caseId];
        var json = EmployeeSignatureInputCases.Payload(propertyName, planned.Json);
        var error = Record.Exception(() => EmployeeSignatureInputCases.Source(json));
        Assert.Equal(planned.Accepted, error is null);
        if (!planned.Accepted) return;
        var signature = EmployeeSignatureInputCases.Source(json);
        var address = EmployeeAddressInputCases.Source(EmployeeAddressInputCases.Payload(propertyName, planned.Json));
        Assert.Equal(address.Id, signature.Id);
        Assert.Equal(address.CreatedDate, signature.CreatedDate);
        Assert.Equal(address.ModifiedDate, signature.ModifiedDate);
        Assert.Equal(address.CreatedDate?.Kind, signature.CreatedDate?.Kind);
        Assert.Equal(address.ModifiedDate?.Kind, signature.ModifiedDate?.Kind);
        Assert.Equal(7, signature.EmployeeId);
        Assert.Equal("literal ไทย", signature.Bucket);
        Assert.Equal("literal ไทย.png", signature.ObjectName);
    }

    /// <summary>Checks source-shaped signature occurrence ordering preserves ignored nulls, converted values, date kinds, and errors.</summary>
    /// <param name="propertyName">The identity or audit property repeated in both source-shaped projections.</param>
    /// <param name="orderId">The sequence identifier selecting replacement, null handling, mixed casing, or unsupported occurrences.</param>
    [Theory]
    [MemberData(nameof(EmployeeSignatureInputCases.OrderCases), MemberType = typeof(EmployeeSignatureInputCases))]
    public void MutableSource_PreservesOrderedNullAndErrors(string propertyName, string orderId)
    {
        var json = EmployeeSignatureInputCases.Ordered(propertyName, orderId);
        var error = Record.Exception(() => EmployeeSignatureInputCases.Source(json));
        Assert.Equal(EmployeeSignatureInputCases.OrderAccepted(orderId), error is null);
        if (error is not null) return;
        var signature = EmployeeSignatureInputCases.Source(json);
        var address = EmployeeAddressInputCases.Source(EmployeeAddressInputCases.Ordered(propertyName, orderId));
        Assert.Equal(address.Id, signature.Id);
        Assert.Equal(address.CreatedDate, signature.CreatedDate);
        Assert.Equal(address.ModifiedDate, signature.ModifiedDate);
        Assert.Equal(address.CreatedDate?.Kind, signature.CreatedDate?.Kind);
        Assert.Equal(address.ModifiedDate?.Kind, signature.ModifiedDate?.Kind);
        Assert.Equal(7, signature.EmployeeId);
    }
}

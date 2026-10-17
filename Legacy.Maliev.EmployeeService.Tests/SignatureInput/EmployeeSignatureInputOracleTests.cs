using Legacy.Maliev.EmployeeService.Tests.AddressInput;

namespace Legacy.Maliev.EmployeeService.Tests.SignatureInput;

public sealed class EmployeeSignatureInputOracleTests
{
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

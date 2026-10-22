using System.Globalization;

namespace Legacy.Maliev.EmployeeService.Tests.AddressInput;

/// <summary>Characterizes public Newtonsoft scalar and mutable address projections using the source-shaped comparison helpers.</summary>
public sealed class EmployeeAddressInputOracleTests
{
    /// <summary>Checks scalar acceptance, integer coercion, and parsed date kinds against the planned public-reader cases.</summary>
    /// <param name="kind">The scalar family selector: id for nullable Int32 parsing or date for nullable DateTime parsing.</param>
    /// <param name="caseId">The scalar case key selecting JSON input and its expected acceptance, integer value, or date kind.</param>
    [Theory]
    [MemberData(nameof(EmployeeAddressInputCases.ReaderCases), MemberType = typeof(EmployeeAddressInputCases))]
    public void PublicScalar_CharacterizesCoercionAndKind(string kind, string caseId)
    {
        var planned = (kind == "id" ? EmployeeAddressInputCases.Integers : EmployeeAddressInputCases.Dates)[caseId];
        object? value = null;
        var error = Record.Exception(() => value = EmployeeAddressInputCases.ReadScalar(kind, planned.Json));
        Assert.Equal(planned.Accepted, error is null);
        if (!planned.Accepted)
        {
            Assert.NotNull(error);
            return;
        }
        if (kind == "id") Assert.Equal(planned.Integer, (int?)value);
        else if (planned.Kind is { } expectedKind)
            Assert.Equal(expectedKind, Assert.IsType<DateTime>(value).Kind);
        else Assert.Null(value);
    }

    /// <summary>Checks that a mutable source-shaped address projects the selected scalar consistently with the public typed reader.</summary>
    /// <param name="propertyName">The address identifier or timestamp property receiving the scalar JSON token.</param>
    /// <param name="caseId">The scalar case key selecting the token and expected acceptance for that property.</param>
    [Theory]
    [MemberData(nameof(EmployeeAddressInputCases.FieldCases), MemberType = typeof(EmployeeAddressInputCases))]
    public void MutableSource_UsesPublicTypedReader(string propertyName, string caseId)
    {
        var planned = EmployeeAddressInputCases.Cases(propertyName)[caseId];
        EmployeeAddressInputCases.SourceAddress? source = null;
        var error = Record.Exception(() => source = EmployeeAddressInputCases.Source(
            EmployeeAddressInputCases.Payload(propertyName, planned.Json)));
        Assert.Equal(planned.Accepted, error is null);
        if (!planned.Accepted)
        {
            Assert.NotNull(error);
            return;
        }
        Assert.NotNull(source);
        var scalar = EmployeeAddressInputCases.ReadScalar(EmployeeAddressInputCases.IsId(propertyName) ? "id" : "date", planned.Json);
        var expected = EmployeeAddressInputCases.IsId(propertyName) ? scalar ?? 0 : scalar;
        Assert.Equal(expected, EmployeeAddressInputCases.SourceValue(source, propertyName));
        if (expected is DateTime date)
            Assert.Equal(date.Kind, Assert.IsType<DateTime>(EmployeeAddressInputCases.SourceValue(source, propertyName)).Kind);
        Assert.Equal("literal ไทย", source.Building);
        Assert.Equal(764, source.CountryId);
    }

    /// <summary>Checks ordered duplicate assignments for retained nonnull values, replacement values, and invalid occurrences.</summary>
    /// <param name="propertyName">The address identifier or timestamp property repeated in the constructed JSON body.</param>
    /// <param name="orderId">The duplicate-assignment scenario selecting null ordering, replacement, mixed casing, or an invalid occurrence.</param>
    [Theory]
    [MemberData(nameof(EmployeeAddressInputCases.OrderCases), MemberType = typeof(EmployeeAddressInputCases))]
    public void MutableSource_PreservesOrderedNullAndErrors(string propertyName, string orderId)
    {
        EmployeeAddressInputCases.SourceAddress? source = null;
        var error = Record.Exception(() => source = EmployeeAddressInputCases.Source(
            EmployeeAddressInputCases.Ordered(propertyName, orderId)));
        Assert.Equal(EmployeeAddressInputCases.OrderAccepted(orderId), error is null);
        if (error is not null) return;
        Assert.NotNull(source);
        var final = orderId is "replace" or "mixed_replace"
            ? EmployeeAddressInputCases.IsId(propertyName) ? "7" : "\"2021-02-03T04:05:06Z\""
            : orderId == "all_null" ? "null"
            : EmployeeAddressInputCases.IsId(propertyName) ? "42" : "\"2020-01-02T03:04:05Z\"";
        var scalar = EmployeeAddressInputCases.ReadScalar(EmployeeAddressInputCases.IsId(propertyName) ? "id" : "date", final);
        Assert.Equal(EmployeeAddressInputCases.IsId(propertyName) ? scalar ?? 0 : scalar,
            EmployeeAddressInputCases.SourceValue(source, propertyName));
    }

    /// <summary>Checks that configured invariant scalar parsing remains independent of the thread culture.</summary>
    /// <param name="culture">The culture name temporarily assigned to the current thread while parsing signed integer text and a month-first date.</param>
    [Theory]
    [InlineData("fr-FR")]
    [InlineData("th-TH")]
    public void ConfiguredInvariantCulture_DoesNotFollowThreadCulture(string culture)
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            Assert.Equal(42, EmployeeAddressInputCases.ReadScalar("id", "\" +42 \""));
            var date = Assert.IsType<DateTime>(EmployeeAddressInputCases.ReadScalar("date", "\"01/02/2020\""));
            Assert.Equal(new DateTime(2020, 1, 2), date);
            Assert.Equal(DateTimeKind.Unspecified, date.Kind);
            Assert.Equal(date, EmployeeAddressInputCases.Source("{\"CreatedDate\":\"01/02/2020\"}").CreatedDate);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }
}

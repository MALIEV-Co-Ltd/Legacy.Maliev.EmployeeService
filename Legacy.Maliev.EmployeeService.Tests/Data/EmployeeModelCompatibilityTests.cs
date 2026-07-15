using Legacy.Maliev.EmployeeService.Data;
using Legacy.Maliev.EmployeeService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Legacy.Maliev.EmployeeService.Tests.Data;

public sealed class EmployeeModelCompatibilityTests
{
    [Fact]
    public void Model_PreservesLegacyTablesColumnsLengthsAndRelationships()
    {
        var options = new DbContextOptionsBuilder<EmployeeDbContext>().UseNpgsql("Host=localhost;Database=model").Options;
        using var context = new EmployeeDbContext(options);

        var employee = context.Model.FindEntityType(typeof(Employee))!;
        var employeeTable = StoreObjectIdentifier.Table("Employee", null);
        Assert.Equal("Employee", employee.GetTableName());
        Assert.Equal("ID", employee.FindProperty(nameof(Employee.Id))!.GetColumnName(employeeTable));
        Assert.Equal("HomeAddressID", employee.FindProperty(nameof(Employee.HomeAddressId))!.GetColumnName(employeeTable));
        Assert.Equal("RoleID", employee.FindProperty(nameof(Employee.RoleId))!.GetColumnName(employeeTable));
        Assert.Equal(513, employee.FindProperty(nameof(Employee.FullName))!.GetMaxLength());
        Assert.Null(employee.FindProperty("xmin"));

        Assert.Equal("FK_Employee_Address", employee.GetForeignKeys().Single(key => key.Properties.Single().Name == nameof(Employee.HomeAddressId)).GetConstraintName());
        Assert.Equal("FK_Employee_Role", employee.GetForeignKeys().Single(key => key.Properties.Single().Name == nameof(Employee.RoleId)).GetConstraintName());

        var signature = context.Model.FindEntityType(typeof(SignatureImageFile))!;
        var signatureTable = StoreObjectIdentifier.Table("SignatureImageFile", null);
        Assert.Equal("EmployeeID", signature.FindProperty(nameof(SignatureImageFile.EmployeeId))!.GetColumnName(signatureTable));
        Assert.Equal(50, signature.FindProperty(nameof(SignatureImageFile.Bucket))!.GetMaxLength());
        Assert.Empty(signature.GetForeignKeys());
    }
}

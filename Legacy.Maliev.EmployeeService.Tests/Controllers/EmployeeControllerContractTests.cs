using System.Reflection;
using Legacy.Maliev.EmployeeService.Api.Controllers;
using Legacy.Maliev.EmployeeService.Application.Models;
using Maliev.Aspire.ServiceDefaults.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Legacy.Maliev.EmployeeService.Tests.Controllers;

public sealed class EmployeeControllerContractTests
{
    [Theory]
    [InlineData(typeof(EmployeesController), "[controller]")]
    [InlineData(typeof(AddressesController), "employees/[controller]")]
    [InlineData(typeof(RolesController), "employees/[controller]")]
    [InlineData(typeof(SignaturesController), "employees/[controller]")]
    public void Controllers_PreserveLegacyBaseRoutesAndRequireAuthentication(Type controller, string route)
    {
        Assert.Equal(route, controller.GetCustomAttribute<RouteAttribute>()?.Template);
        Assert.NotNull(controller.GetCustomAttribute<AuthorizeAttribute>());
    }

    [Fact]
    public void EmployeeActions_PreserveAllLegacyTemplates()
    {
        AssertAction<EmployeesController>(nameof(EmployeesController.ValidateUserCredentialsAsync), "v1/validate", typeof(HttpPostAttribute));
        AssertAction<EmployeesController>(nameof(EmployeesController.CreateEmployeeAsync), null, typeof(HttpPostAttribute));
        AssertAction<EmployeesController>(nameof(EmployeesController.CreateIdentityAsync), "{id:int}/identity/{password?}", typeof(HttpPostAttribute));
        AssertAction<EmployeesController>(nameof(EmployeesController.DeleteEmployeeAsync), "{id:int}", typeof(HttpDeleteAttribute));
        AssertAction<EmployeesController>(nameof(EmployeesController.DeleteIdentityAsync), "{id:int}/identity", typeof(HttpDeleteAttribute));
        AssertAction<EmployeesController>(nameof(EmployeesController.GetEmployeeAsync), "{employeeId:int}", typeof(HttpGetAttribute));
        AssertAction<EmployeesController>(nameof(EmployeesController.GetIdentityAsync), "{id:int}/identity", typeof(HttpGetAttribute));
        AssertAction<EmployeesController>(nameof(EmployeesController.GetPaginatedAsync), null, typeof(HttpGetAttribute));
        AssertAction<EmployeesController>(nameof(EmployeesController.UpdateEmployeeAsync), "{id:int}", typeof(HttpPutAttribute));
        AssertAction<EmployeesController>(nameof(EmployeesController.UpdateIdentityAsync), "{id:int}/identity", typeof(HttpPutAttribute));
    }

    [Fact]
    public void RelatedControllers_PreserveFourteenLegacyActions()
    {
        Assert.Equal(5, PublicActions<AddressesController>());
        Assert.Equal(5, PublicActions<RolesController>());
        Assert.Equal(4, PublicActions<SignaturesController>());
        AssertAction<SignaturesController>(nameof(SignaturesController.CreateSignatureImageFileEntryAsync), "/employees/{employeeId:int}/[controller]", typeof(HttpPostAttribute));
        AssertAction<SignaturesController>(nameof(SignaturesController.GetSignatureImageFileAsync), "{employeeId:int}", typeof(HttpGetAttribute));
    }

    [Fact]
    public void CredentialValidation_IsProtectedAndLiveCritical()
    {
        var method = typeof(EmployeesController).GetMethod(nameof(EmployeesController.ValidateUserCredentialsAsync))!;
        Assert.Null(method.GetCustomAttribute<AllowAnonymousAttribute>());
        var permission = Assert.Single(method.GetCustomAttributes<RequirePermissionAttribute>());
        Assert.True(permission.RequireLiveCheck);
        Assert.True(permission.IsCritical);
    }

    [Fact]
    public void IdentityCompatibilityDto_DoesNotExposeCredentialSecrets()
    {
        var names = typeof(EmployeeIdentityResponse).GetProperties().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain("PasswordHash", names);
        Assert.DoesNotContain("SecurityStamp", names);
        Assert.DoesNotContain("AuthenticatorKey", names);
        Assert.Contains("DatabaseID", names);
    }

    private static int PublicActions<TController>() => typeof(TController).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Length;

    private static void AssertAction<TController>(string methodName, string? template, Type attributeType)
    {
        var method = typeof(TController).GetMethod(methodName)!;
        var attribute = Assert.Single(method.GetCustomAttributes(), attributeType.IsInstanceOfType);
        Assert.Equal(template, ((HttpMethodAttribute)attribute).Template);
        Assert.Single(method.GetCustomAttributes<RequirePermissionAttribute>());
    }
}

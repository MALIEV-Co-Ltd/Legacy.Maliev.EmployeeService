using System.Reflection;
using Legacy.Maliev.EmployeeService.Api.Controllers;
using Maliev.Aspire.ServiceDefaults.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Legacy.Maliev.EmployeeService.Tests.Controllers;

public sealed class EmployeeControllerContractTests
{
    private static readonly Type[] ControllerTypes = [typeof(EmployeesController), typeof(AddressesController), typeof(RolesController), typeof(SignaturesController)];
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
        AssertAction<EmployeesController>(nameof(EmployeesController.CreateEmployeeAsync), null, typeof(HttpPostAttribute));
        AssertAction<EmployeesController>(nameof(EmployeesController.DeleteEmployeeAsync), "{id:int}", typeof(HttpDeleteAttribute));
        AssertAction<EmployeesController>(nameof(EmployeesController.GetEmployeeAsync), "{employeeId:int}", typeof(HttpGetAttribute));
        AssertAction<EmployeesController>(nameof(EmployeesController.GetPaginatedAsync), null, typeof(HttpGetAttribute));
        AssertAction<EmployeesController>(nameof(EmployeesController.UpdateEmployeeAsync), "{id:int}", typeof(HttpPutAttribute));
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
    public void EmployeeApi_DoesNotExposeIdentityOrCredentialOperations()
    {
        var actions = typeof(EmployeesController).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        Assert.DoesNotContain(actions, method =>
            method.Name.Contains("Identity", StringComparison.OrdinalIgnoreCase)
            || method.Name.Contains("Credential", StringComparison.OrdinalIgnoreCase)
            || method.GetCustomAttributes<HttpMethodAttribute>().Any(attribute =>
                attribute.Template?.Contains("identity", StringComparison.OrdinalIgnoreCase) == true
                || attribute.Template?.Contains("password", StringComparison.OrdinalIgnoreCase) == true
                || attribute.Template?.Contains("validate", StringComparison.OrdinalIgnoreCase) == true));
    }

    [Fact]
    public void SignedPermissionClaims_AreAuthoritativeExceptForCriticalDestructiveActions()
    {
        var actions = ControllerTypes.SelectMany(controller =>
            controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly));
        Assert.All(actions, action =>
        {
            var permission = Assert.Single(action.GetCustomAttributes<RequirePermissionAttribute>());
            if (action.GetCustomAttribute<HttpDeleteAttribute>() is not null)
            {
                Assert.True(permission.RequireLiveCheck);
                Assert.True(permission.IsCritical);
            }
            else
            {
                Assert.False(permission.RequireLiveCheck);
            }
        });
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

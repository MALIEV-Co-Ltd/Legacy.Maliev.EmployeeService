using System.Reflection;
using System.Text.Json;
using Legacy.Maliev.EmployeeService.Api.Controllers;
using Legacy.Maliev.EmployeeService.Api.Authorization;
using Legacy.Maliev.EmployeeService.Application.Interfaces;
using Legacy.Maliev.EmployeeService.Application.Models;
using Maliev.Aspire.ServiceDefaults.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Moq;

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
        AssertAction<EmployeesController>(nameof(EmployeesController.UpdateSelfProfileAsync), "{employeeId:int}/profile", typeof(HttpPutAttribute));
    }

    [Fact]
    public void SelfProfileRequest_ExposesOnlyEmployeeOwnedFieldsAndRejectsUnknownMembers()
    {
        Assert.Equal(
            ["DateOfBirth", "FirstName", "LastName", "PhoneNumber"],
            typeof(UpdateEmployeeSelfProfileRequest).GetProperties().Select(property => property.Name).Order());

        var json = """{"FirstName":"Ada","LastName":"Lovelace","PhoneNumber":"0690","DateOfBirth":"1815-12-10","RoleId":9}""";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UpdateEmployeeSelfProfileRequest>(json));
    }

    [Fact]
    public void SelfProfileAction_UsesProfilePermissionAndEmployeeScopedResourcePath()
    {
        var action = typeof(EmployeesController).GetMethod(nameof(EmployeesController.UpdateSelfProfileAsync))!;
        var permission = Assert.Single(action.GetCustomAttributes<RequirePermissionAttribute>());

        Assert.Equal(EmployeePermissions.EmployeesSelfUpdate, permission.Permission);
        Assert.Equal("legacy-employee.employees.self-update", permission.Permission);
        Assert.Equal("/employees/{employeeId}/profile", permission.ResourcePathTemplate);
        Assert.False(permission.RequireLiveCheck);
    }

    [Fact]
    public async Task UpdateSelfProfileAsync_AuthorizedBffService_UpdatesOnlyNarrowProfile()
    {
        var request = ProfileRequest();
        var service = new Mock<IEmployeeService>(MockBehavior.Strict);
        service.Setup(value => value.UpdateSelfProfileAsync(7, request, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var controller = new EmployeesController(service.Object);

        var result = await controller.UpdateSelfProfileAsync(7, request, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        service.VerifyAll();
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

    private static UpdateEmployeeSelfProfileRequest ProfileRequest() =>
        new("Ada", "Lovelace", "0690", new DateTime(1815, 12, 10));

    private static void AssertAction<TController>(string methodName, string? template, Type attributeType)
    {
        var method = typeof(TController).GetMethod(methodName)!;
        var attribute = Assert.Single(method.GetCustomAttributes(), attributeType.IsInstanceOfType);
        Assert.Equal(template, ((HttpMethodAttribute)attribute).Template);
        Assert.Single(method.GetCustomAttributes<RequirePermissionAttribute>());
    }
}

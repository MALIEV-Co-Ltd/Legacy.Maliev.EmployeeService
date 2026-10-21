using Legacy.Maliev.EmployeeService.Api.Controllers;
using Legacy.Maliev.EmployeeService.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Legacy.Maliev.EmployeeService.Tests.Controllers;

/// <summary>Verifies direct role-controller rejection of null CLR requests before any service call.</summary>
public sealed class EmployeeOriginalRoleNullControllerTests
{
    /// <summary>Checks that a null create request returns the original role-required 400 response without calling the service.</summary>
    /// <returns>A task that completes after verifying that a null create request returns the original role-required 400 response without calling the service.</returns>
    [Fact]
    public async Task NullCreate_PreservesOriginalClrBadRequestBeforeAnyServiceCall()
    {
        var service = new Mock<IEmployeeService>(MockBehavior.Strict);
        var controller = new RolesController(service.Object);

        var result = await controller.CreateRoleAsync(null!, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, badRequest.StatusCode);
        Assert.Equal("Role data is required", Assert.IsType<string>(badRequest.Value));
        service.VerifyNoOtherCalls();
    }

    /// <summary>Checks that a null update request returns the role-required 400 response before role lookup.</summary>
    /// <param name="roleId">The existing-shaped or missing-shaped role identifier used to prove null rejection occurs before lookup.</param>
    /// <returns>A task that completes after verifying that a null update request returns the role-required 400 response before role lookup.</returns>
    [Theory]
    [InlineData(13)]
    [InlineData(int.MaxValue)]
    public async Task NullUpdate_PreservesOriginalClrBadRequestBeforeRoleLookup(int roleId)
    {
        var service = new Mock<IEmployeeService>(MockBehavior.Strict);
        var controller = new RolesController(service.Object);

        var result = await controller.UpdateRoleAsync(roleId, null!, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, badRequest.StatusCode);
        Assert.Equal("Role data is required", Assert.IsType<string>(badRequest.Value));
        service.VerifyNoOtherCalls();
    }
}

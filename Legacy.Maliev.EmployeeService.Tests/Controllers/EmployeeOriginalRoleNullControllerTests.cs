using Legacy.Maliev.EmployeeService.Api.Controllers;
using Legacy.Maliev.EmployeeService.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Legacy.Maliev.EmployeeService.Tests.Controllers;

public sealed class EmployeeOriginalRoleNullControllerTests
{
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

using Legacy.Maliev.EmployeeService.Api.Controllers;
using Legacy.Maliev.EmployeeService.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Legacy.Maliev.EmployeeService.Tests.Controllers;

public sealed class EmployeeOriginalAddressNullControllerTests
{
    [Fact]
    public async Task NullCreate_PreservesOriginalBadRequestMessageBeforeAnyServiceCall()
    {
        var service = new Mock<IEmployeeService>(MockBehavior.Strict);
        var controller = new AddressesController(service.Object);

        var result = await controller.CreateAddressAsync(null!, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, badRequest.StatusCode);
        Assert.Equal("Address data is required", Assert.IsType<string>(badRequest.Value));
        service.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(101)]
    [InlineData(int.MaxValue)]
    public async Task NullUpdate_PreservesOriginalBadRequestBeforeAddressLookup(int addressId)
    {
        var service = new Mock<IEmployeeService>(MockBehavior.Strict);
        var controller = new AddressesController(service.Object);

        var result = await controller.UpdateAddressAsync(addressId, null!, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, badRequest.StatusCode);
        Assert.Equal("Address data is required", Assert.IsType<string>(badRequest.Value));
        service.VerifyNoOtherCalls();
    }
}

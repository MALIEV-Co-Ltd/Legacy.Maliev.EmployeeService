using Legacy.Maliev.EmployeeService.Api.Controllers;
using Legacy.Maliev.EmployeeService.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Legacy.Maliev.EmployeeService.Tests.Controllers;

/// <summary>Checks that null address requests reach the original explicit bad-request guard before service access.</summary>
public sealed class EmployeeOriginalAddressNullControllerTests
{
    /// <summary>Checks that a null create request returns the original address-required message without calling the employee service.</summary>
    /// <returns>A task that completes after asserting the 400 response, exact guard message, and absence of service calls.</returns>
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

    /// <summary>Checks that a null update request is refused before address lookup regardless of the requested identifier.</summary>
    /// <param name="addressId">The existing-shaped or maximum Int32 route identifier that must not trigger a service lookup for a null body.</param>
    /// <returns>A task that completes after asserting the original 400 guard response and no service calls.</returns>
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

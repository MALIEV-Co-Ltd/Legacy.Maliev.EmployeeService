using Legacy.Maliev.EmployeeService.Application.Interfaces;
using Legacy.Maliev.EmployeeService.Application.Models;
using Legacy.Maliev.EmployeeService.Application.Services;
using Moq;

namespace Legacy.Maliev.EmployeeService.Tests.Application;

public sealed class EmployeeApplicationServiceTests
{
    [Fact]
    public async Task GetEmployeeAsync_CacheHit_DoesNotQueryPostgreSql()
    {
        var cached = SampleEmployee();
        var repository = new Mock<IEmployeeRepository>(MockBehavior.Strict);
        var cache = new Mock<IEmployeeCache>();
        cache.Setup(value => value.GetAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(cached);
        var service = new EmployeeApplicationService(repository.Object, cache.Object);

        var result = await service.GetEmployeeAsync(7, CancellationToken.None);

        Assert.Same(cached, result);
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateAddressAsync_InvalidatesEveryEmployeeUsingAddress()
    {
        var repository = new Mock<IEmployeeRepository>();
        repository.Setup(value => value.GetEmployeeIdsForAddressAsync(13, It.IsAny<CancellationToken>())).ReturnsAsync([7, 8]);
        repository.Setup(value => value.UpdateAddressAsync(13, It.IsAny<UpsertAddressRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var cache = new Mock<IEmployeeCache>();
        var service = new EmployeeApplicationService(repository.Object, cache.Object);

        var result = await service.UpdateAddressAsync(13, new UpsertAddressRequest(null, "1 Legacy Road", null, "Bangkok", null, "10110", 764), CancellationToken.None);

        Assert.True(result);
        cache.Verify(value => value.RemoveAsync(7, It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(value => value.RemoveAsync(8, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetEmployeesAsync_ClampsUnboundedLegacyRequest()
    {
        var repository = new Mock<IEmployeeRepository>();
        repository.Setup(value => value.GetEmployeesAsync(null, null, 1, 250, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaginatedResponse<EmployeeResponse>?)null);
        var service = new EmployeeApplicationService(repository.Object, Mock.Of<IEmployeeCache>());

        await service.GetEmployeesAsync(null, null, null, 10_000, CancellationToken.None);

        repository.VerifyAll();
    }

    [Fact]
    public async Task UpdateSelfProfileAsync_Success_InvalidatesOnlyOwnedEmployeeCache()
    {
        var request = new UpdateEmployeeSelfProfileRequest("Ada", "Lovelace", "0690", new DateTime(1815, 12, 10));
        var repository = new Mock<IEmployeeRepository>(MockBehavior.Strict);
        repository.Setup(value => value.UpdateSelfProfileAsync(7, request, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var cache = new Mock<IEmployeeCache>(MockBehavior.Strict);
        cache.Setup(value => value.RemoveAsync(7, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var service = new EmployeeApplicationService(repository.Object, cache.Object);

        var updated = await service.UpdateSelfProfileAsync(7, request, CancellationToken.None);

        Assert.True(updated);
        repository.VerifyAll();
        cache.VerifyAll();
    }

    private static EmployeeResponse SampleEmployee() => new(7, 2, "Ada", "Lovelace", "Ada Lovelace", null, "ada@example.com", null, null, null, null, null, null);
}

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
        var service = new EmployeeApplicationService(repository.Object, cache.Object, Mock.Of<IEmployeeIdentityDirectory>());

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
        var service = new EmployeeApplicationService(repository.Object, cache.Object, Mock.Of<IEmployeeIdentityDirectory>());

        var result = await service.UpdateAddressAsync(13, new UpsertAddressRequest(null, "1 Legacy Road", null, "Bangkok", null, "10110", 764), CancellationToken.None);

        Assert.True(result);
        cache.Verify(value => value.RemoveAsync(7, It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(value => value.RemoveAsync(8, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateIdentityAsync_UnknownEmployee_DoesNotCallAuthService()
    {
        var repository = new Mock<IEmployeeRepository>();
        repository.Setup(value => value.GetEmployeeAsync(99, It.IsAny<CancellationToken>())).ReturnsAsync((EmployeeResponse?)null);
        var identities = new Mock<IEmployeeIdentityDirectory>(MockBehavior.Strict);
        var service = new EmployeeApplicationService(repository.Object, Mock.Of<IEmployeeCache>(), identities.Object);

        var result = await service.CreateIdentityAsync(99, SampleIdentity(), "temporary-password", CancellationToken.None);

        Assert.False(result.Succeeded);
        identities.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetEmployeesAsync_ClampsUnboundedLegacyRequest()
    {
        var repository = new Mock<IEmployeeRepository>();
        repository.Setup(value => value.GetEmployeesAsync(null, null, 1, 250, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaginatedResponse<EmployeeResponse>?)null);
        var service = new EmployeeApplicationService(repository.Object, Mock.Of<IEmployeeCache>(), Mock.Of<IEmployeeIdentityDirectory>());

        await service.GetEmployeesAsync(null, null, null, 10_000, CancellationToken.None);

        repository.VerifyAll();
    }

    private static EmployeeResponse SampleEmployee() => new(7, 2, "Ada", "Lovelace", "Ada Lovelace", null, "ada@example.com", null, null, null, null, null, null);
    private static EmployeeIdentityRequest SampleIdentity() => new(null, "ada@example.com", "ada@example.com", false, null, false, false, null, true, 0, 99, null, null);
}

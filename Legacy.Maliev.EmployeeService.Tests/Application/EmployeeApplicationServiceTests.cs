using Legacy.Maliev.EmployeeService.Application.Interfaces;
using Legacy.Maliev.EmployeeService.Application.Models;
using Legacy.Maliev.EmployeeService.Application.Services;
using Moq;

namespace Legacy.Maliev.EmployeeService.Tests.Application;

/// <summary>Verifies repository authority and cache cleanup behavior at the Employee application-service boundary.</summary>
public sealed class EmployeeApplicationServiceTests
{
    /// <summary>Checks that employee detail uses the fresh repository result without reading or publishing cached projections.</summary>
    /// <returns>A task that completes after verifying that employee detail uses the fresh repository result without reading or publishing cached projections.</returns>
    [Fact]
    public async Task GetEmployeeAsync_StaleCacheCannotReplaceAuthoritativePostgreSqlResult()
    {
        var cached = SampleEmployee();
        var fresh = cached with { FirstName = "Fresh" };
        var repository = new Mock<IEmployeeRepository>(MockBehavior.Strict);
        repository.Setup(value => value.GetEmployeeAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(fresh);
        var cache = new Mock<IEmployeeCache>();
        cache.Setup(value => value.GetAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(cached);
        var service = new EmployeeApplicationService(repository.Object, cache.Object);

        var result = await service.GetEmployeeAsync(7, CancellationToken.None);

        Assert.Same(fresh, result);
        repository.Verify(value => value.GetEmployeeAsync(7, It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(value => value.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        cache.Verify(value => value.SetAsync(It.IsAny<Legacy.Maliev.EmployeeService.Application.Models.EmployeeResponse>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.VerifyNoOtherCalls();
    }

    /// <summary>Checks that an address update invalidates every employee projection linked to that address.</summary>
    /// <returns>A task that completes after verifying that an address update invalidates every employee projection linked to that address.</returns>
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

    /// <summary>Checks that legacy directory sizes pass unchanged to the repository while an omitted page index defaults to one.</summary>
    /// <returns>A task that completes after verifying that legacy directory sizes pass unchanged to the repository while an omitted page index defaults to one.</returns>
    [Fact]
    public async Task GetEmployeesAsync_ClampsUnboundedLegacyRequest()
    {
        // Retain this historical Fact identity while replacing the unsupported
        // migration clamp oracle with the original unmodified size forwarding.
        var repository = new Mock<IEmployeeRepository>(MockBehavior.Strict);
        int?[] sizes = [null, 0, -1, 251, 10_000, int.MaxValue];
        foreach (var size in sizes)
            repository.Setup(value => value.GetEmployeesAsync(null, null, 1, size, It.IsAny<CancellationToken>()))
                .ReturnsAsync((PaginatedResponse<EmployeeResponse>?)null);
        var service = new EmployeeApplicationService(repository.Object, Mock.Of<IEmployeeCache>());

        foreach (var size in sizes)
            await service.GetEmployeesAsync(null, null, null, size, CancellationToken.None);

        foreach (var size in sizes)
            repository.Verify(value => value.GetEmployeesAsync(null, null, 1, size, It.IsAny<CancellationToken>()), Times.Once);
        repository.VerifyNoOtherCalls();
    }

    /// <summary>Checks that successful self-profile updates invalidate only the owning employee projection.</summary>
    /// <returns>A task that completes after verifying that successful self-profile updates invalidate only the owning employee projection.</returns>
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

    /// <summary>Checks that caller cancellation after a committed conditional result cannot cancel cache cleanup.</summary>
    /// <param name="address">Whether to update a shared address and invalidate both linked employees rather than update one employee.</param>
    /// <returns>A task that completes after verifying that caller cancellation after a committed conditional result cannot cancel cache cleanup.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConditionalEdit_CancellationAfterCommittedResult_CannotCancelCacheCleanup(bool address)
    {
        using var cancellation = new CancellationTokenSource();
        var repository = new Mock<IEmployeeRepository>(MockBehavior.Strict);
        var cache = new Mock<IEmployeeCache>(MockBehavior.Strict);
        var committed = new EmployeeEditResult(EmployeeEditOutcome.Updated, "new-version");
        if (address)
        {
            repository.Setup(value => value.GetEmployeeIdsForAddressAsync(7, cancellation.Token)).ReturnsAsync([7, 8]);
            repository.Setup(value => value.UpdateAddressIfMatchAsync(7, It.IsAny<UpsertAddressRequest>(), "version", cancellation.Token))
                .Callback(() => cancellation.Cancel()).ReturnsAsync(committed);
            cache.Setup(value => value.RemoveAsync(8, CancellationToken.None)).Returns(Task.CompletedTask);
        }
        else
        {
            repository.Setup(value => value.UpdateEmployeeIfMatchAsync(7, It.IsAny<UpsertEmployeeRequest>(), "version", cancellation.Token))
                .Callback(() => cancellation.Cancel()).ReturnsAsync(committed);
        }
        cache.Setup(value => value.RemoveAsync(7, CancellationToken.None)).Returns(Task.CompletedTask);
        var service = new EmployeeApplicationService(repository.Object, cache.Object);
        var actual = address
            ? await service.UpdateAddressIfMatchAsync(7, new UpsertAddressRequest(null, "Road", null, null, null, null, 764), "version", cancellation.Token)
            : await service.UpdateEmployeeIfMatchAsync(7, new UpsertEmployeeRequest(null, "Ada", "Fixture", null, "ada@example.test", null, null), "version", cancellation.Token);
        Assert.Same(committed, actual);
        Assert.True(cancellation.IsCancellationRequested);
        repository.VerifyAll();
        cache.VerifyAll();
    }

    /// <summary>Checks that cache-cleanup failures do not replace the original caller-cancellation exception.</summary>
    /// <param name="address">Whether the canceled conditional operation updates an address rather than an employee.</param>
    /// <returns>A task that completes after verifying that cache-cleanup failures do not replace the original caller-cancellation exception.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConditionalEdit_CallerCancellationIsPreservedEvenWhenCacheCleanupFails(bool address)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var canceled = new OperationCanceledException(cancellation.Token);
        var repository = new Mock<IEmployeeRepository>(MockBehavior.Strict);
        var cache = new Mock<IEmployeeCache>(MockBehavior.Strict);
        if (address)
        {
            repository.Setup(value => value.GetEmployeeIdsForAddressAsync(7, cancellation.Token)).ReturnsAsync([7]);
            repository.Setup(value => value.UpdateAddressIfMatchAsync(7, It.IsAny<UpsertAddressRequest>(), "version", cancellation.Token)).ThrowsAsync(canceled);
        }
        else
        {
            repository.Setup(value => value.UpdateEmployeeIfMatchAsync(7, It.IsAny<UpsertEmployeeRequest>(), "version", cancellation.Token)).ThrowsAsync(canceled);
        }
        cache.Setup(value => value.RemoveAsync(7, CancellationToken.None)).ThrowsAsync(new InvalidOperationException("Cache unavailable"));
        var service = new EmployeeApplicationService(repository.Object, cache.Object);
        var actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            if (address) await service.UpdateAddressIfMatchAsync(7, new UpsertAddressRequest(null, "Road", null, null, null, null, 764), "version", cancellation.Token);
            else await service.UpdateEmployeeIfMatchAsync(7, new UpsertEmployeeRequest(null, "Ada", "Fixture", null, "ada@example.test", null, null), "version", cancellation.Token);
        });
        Assert.Same(canceled, actual);
        repository.VerifyAll();
        cache.VerifyAll();
    }

    private static EmployeeResponse SampleEmployee() => new(7, 2, "Ada", "Lovelace", "Ada Lovelace", null, "ada@example.com", null, null, null, null, null, null);

    /// <summary>Checks that home-address cache cleanup remains uncancellable after the repository returns a committed result.</summary>
    /// <returns>A task that completes after verifying that home-address cache cleanup remains uncancellable after the repository returns a committed result.</returns>
    [Fact]
    public async Task HomeAddressConditionalEdit_PostReturnCancellationCannotCancelLinkedCacheCleanup()
    {
        using var cancellation = new CancellationTokenSource();
        var repository = new Mock<IEmployeeRepository>(MockBehavior.Strict);
        var cache = new Mock<IEmployeeCache>(MockBehavior.Strict);
        var committed = new EmployeeHomeAddressEditResult(EmployeeEditOutcome.Updated, "address-version", "employee-version", [7, 8]);
        repository.Setup(value => value.UpdateHomeAddressIfMatchAsync(7, It.IsAny<EmployeeHomeAddressEditRequest>(), "employee-version", "address-version", cancellation.Token))
            .Callback(() => cancellation.Cancel()).ReturnsAsync(committed);
        cache.Setup(value => value.RemoveAsync(7, CancellationToken.None)).Returns(Task.CompletedTask);
        cache.Setup(value => value.RemoveAsync(8, CancellationToken.None)).Returns(Task.CompletedTask);
        var service = new EmployeeApplicationService(repository.Object, cache.Object);
        Assert.Same(committed, await service.UpdateHomeAddressIfMatchAsync(7,
            new EmployeeHomeAddressEditRequest(13, null, "Road", null, null, null, null, 764), "employee-version", "address-version", cancellation.Token));
        repository.VerifyAll();
        cache.VerifyAll();
    }

    /// <summary>Checks that home-address cancellation survives cache-removal failure and retains linked-employee cleanup.</summary>
    /// <param name="commitAttempted">Whether cancellation carries the two affected employee identifiers from an attempted commit rather than a plain caller-cancellation exception.</param>
    /// <returns>A task that completes after verifying that home-address cancellation survives cache-removal failure and retains linked-employee cleanup.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HomeAddressConditionalEdit_CallerCancellationSurvivesCacheCleanupFailure(bool commitAttempted)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var error = commitAttempted
            ? new EmployeeHomeAddressCanceledException([7, 8], cancellation.Token)
            : new OperationCanceledException(cancellation.Token);
        var repository = new Mock<IEmployeeRepository>(MockBehavior.Strict);
        var cache = new Mock<IEmployeeCache>(MockBehavior.Strict);
        repository.Setup(value => value.UpdateHomeAddressIfMatchAsync(7, It.IsAny<EmployeeHomeAddressEditRequest>(), "employee-version", "address-version", cancellation.Token))
            .ThrowsAsync(error);
        cache.Setup(value => value.RemoveAsync(7, CancellationToken.None)).ThrowsAsync(new InvalidOperationException("Cache unavailable"));
        if (commitAttempted) cache.Setup(value => value.RemoveAsync(8, CancellationToken.None)).Returns(Task.CompletedTask);
        var service = new EmployeeApplicationService(repository.Object, cache.Object);
        var actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.UpdateHomeAddressIfMatchAsync(7,
            new EmployeeHomeAddressEditRequest(13, null, "Road", null, null, null, null, 764), "employee-version", "address-version", cancellation.Token));
        Assert.Same(error, actual);
        repository.VerifyAll();
        cache.VerifyAll();
    }
}

using Legacy.Maliev.EmployeeService.Application.Models;
using Legacy.Maliev.EmployeeService.Data;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Legacy.Maliev.EmployeeService.Tests.Caching;

public sealed class DistributedEmployeeCacheTests
{
    [Fact]
    public async Task GetAsync_RedisFailure_FailsOpen()
    {
        var distributed = new Mock<IDistributedCache>();
        distributed.Setup(value => value.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("redis unavailable"));
        var cache = new DistributedEmployeeCache(distributed.Object, NullLogger<DistributedEmployeeCache>.Instance);

        var result = await cache.GetAsync(42, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task SetAndGet_RoundTripsEmployeeWithScopedKey()
    {
        byte[]? stored = null;
        var distributed = new Mock<IDistributedCache>();
        distributed.Setup(value => value.SetAsync("employee:42", It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>((_, bytes, _, _) => stored = bytes)
            .Returns(Task.CompletedTask);
        distributed.Setup(value => value.GetAsync("employee:42", It.IsAny<CancellationToken>())).ReturnsAsync(() => stored);
        var cache = new DistributedEmployeeCache(distributed.Object, NullLogger<DistributedEmployeeCache>.Instance);
        var employee = new EmployeeResponse(42, 2, "Ada", "Lovelace", "Ada Lovelace", null, "ada@example.com", null, null, null, null, null, null);

        await cache.SetAsync(employee, CancellationToken.None);
        var result = await cache.GetAsync(42, CancellationToken.None);

        Assert.Equal(employee, result);
    }

    [Fact]
    public async Task GetAsync_does_not_swallow_cancellation()
    {
        var cancellationToken = new CancellationToken(canceled: true);
        var distributed = new Mock<IDistributedCache>();
        distributed.Setup(value => value.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cancellationToken));
        var cache = new DistributedEmployeeCache(distributed.Object, NullLogger<DistributedEmployeeCache>.Instance);

        await Assert.ThrowsAsync<OperationCanceledException>(() => cache.GetAsync(42, cancellationToken));
    }

    [Fact]
    public async Task SetAsync_does_not_swallow_cancellation()
    {
        var cancellationToken = new CancellationToken(canceled: true);
        var distributed = new Mock<IDistributedCache>();
        distributed.Setup(value => value.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cancellationToken));
        var cache = new DistributedEmployeeCache(distributed.Object, NullLogger<DistributedEmployeeCache>.Instance);
        var employee = new EmployeeResponse(42, 2, "Ada", "Lovelace", "Ada Lovelace", null, "ada@example.com", null, null, null, null, null, null);

        await Assert.ThrowsAsync<OperationCanceledException>(() => cache.SetAsync(employee, cancellationToken));
    }

    [Fact]
    public async Task RemoveAsync_does_not_swallow_cancellation()
    {
        var cancellationToken = new CancellationToken(canceled: true);
        var distributed = new Mock<IDistributedCache>();
        distributed.Setup(value => value.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cancellationToken));
        var cache = new DistributedEmployeeCache(distributed.Object, NullLogger<DistributedEmployeeCache>.Instance);

        await Assert.ThrowsAsync<OperationCanceledException>(() => cache.RemoveAsync(42, cancellationToken));
    }
}

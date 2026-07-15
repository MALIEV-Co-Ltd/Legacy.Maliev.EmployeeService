using System.Text.Json;
using Legacy.Maliev.EmployeeService.Application.Interfaces;
using Legacy.Maliev.EmployeeService.Application.Models;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.EmployeeService.Data;

/// <summary>
/// Provides short-lived Redis caching for employee lookups while failing open to PostgreSQL when caching is unavailable.
/// </summary>
/// <param name="cache">The distributed cache used to store serialized employee projections.</param>
/// <param name="logger">The logger used to record cache failures that require a PostgreSQL fallback.</param>
public sealed class DistributedEmployeeCache(IDistributedCache cache, ILogger<DistributedEmployeeCache> logger) : IEmployeeCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly DistributedCacheEntryOptions EntryOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
        SlidingExpiration = TimeSpan.FromMinutes(2),
    };

    /// <inheritdoc />
    public async Task<EmployeeResponse?> GetAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await cache.GetAsync(Key(id), cancellationToken);
            return bytes is null ? null : JsonSerializer.Deserialize<EmployeeResponse>(bytes, JsonOptions);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Employee cache read failed; using PostgreSQL");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SetAsync(EmployeeResponse employee, CancellationToken cancellationToken)
    {
        try
        {
            await cache.SetAsync(Key(employee.Id), JsonSerializer.SerializeToUtf8Bytes(employee, JsonOptions), EntryOptions, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Employee cache write failed; continuing without cache");
        }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            await cache.RemoveAsync(Key(id), cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Employee cache invalidation failed");
        }
    }

    private static string Key(int id) => $"employee:{id}";
}

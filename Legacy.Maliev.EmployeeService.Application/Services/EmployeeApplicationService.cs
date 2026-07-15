using Legacy.Maliev.EmployeeService.Application.Interfaces;
using Legacy.Maliev.EmployeeService.Application.Models;

namespace Legacy.Maliev.EmployeeService.Application.Services;

/// <summary>Coordinates employee profile persistence and cache invalidation.</summary>
public sealed class EmployeeApplicationService(
    IEmployeeRepository repository,
    IEmployeeCache cache) : IEmployeeService
{
    /// <inheritdoc />
    public async Task<EmployeeResponse?> GetEmployeeAsync(int id, CancellationToken cancellationToken)
    {
        var cached = await cache.GetAsync(id, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var employee = await repository.GetEmployeeAsync(id, cancellationToken);
        if (employee is not null)
        {
            await cache.SetAsync(employee, cancellationToken);
        }

        return employee;
    }

    /// <inheritdoc />
    public Task<PaginatedResponse<EmployeeResponse>?> GetEmployeesAsync(EmployeeSortType? sort, string? search, int? index, int? size, CancellationToken cancellationToken) =>
        repository.GetEmployeesAsync(sort, search, Math.Max(index ?? 1, 1), Math.Clamp(size ?? 50, 1, 250), cancellationToken);

    /// <inheritdoc />
    public async Task<EmployeeResponse> CreateEmployeeAsync(UpsertEmployeeRequest request, CancellationToken cancellationToken)
    {
        var entity = await repository.CreateEmployeeAsync(request, cancellationToken);
        return (await repository.GetEmployeeAsync(entity.Id, cancellationToken))!;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateEmployeeAsync(int id, UpsertEmployeeRequest request, CancellationToken cancellationToken)
    {
        var updated = await repository.UpdateEmployeeAsync(id, request, cancellationToken);
        if (updated)
        {
            await cache.RemoveAsync(id, cancellationToken);
        }

        return updated;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteEmployeeAsync(int id, CancellationToken cancellationToken)
    {
        var deleted = await repository.DeleteEmployeeAsync(id, cancellationToken);
        if (deleted)
        {
            await cache.RemoveAsync(id, cancellationToken);
        }

        return deleted;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AddressResponse>> GetAddressesAsync(CancellationToken cancellationToken) => repository.GetAddressesAsync(cancellationToken);
    /// <inheritdoc />
    public Task<AddressResponse?> GetAddressAsync(int id, CancellationToken cancellationToken) => repository.GetAddressAsync(id, cancellationToken);
    /// <inheritdoc />
    public async Task<AddressResponse> CreateAddressAsync(UpsertAddressRequest request, CancellationToken cancellationToken)
    {
        var entity = await repository.CreateAddressAsync(request, cancellationToken);
        return (await repository.GetAddressAsync(entity.Id, cancellationToken))!;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAddressAsync(int id, UpsertAddressRequest request, CancellationToken cancellationToken)
    {
        var employeeIds = await repository.GetEmployeeIdsForAddressAsync(id, cancellationToken);
        var updated = await repository.UpdateAddressAsync(id, request, cancellationToken);
        if (updated)
        {
            await InvalidateEmployeesAsync(employeeIds, cancellationToken);
        }

        return updated;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAddressAsync(int id, CancellationToken cancellationToken)
    {
        var employeeIds = await repository.GetEmployeeIdsForAddressAsync(id, cancellationToken);
        var deleted = await repository.DeleteAddressAsync(id, cancellationToken);
        if (deleted)
        {
            await InvalidateEmployeesAsync(employeeIds, cancellationToken);
        }

        return deleted;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RoleResponse>> GetRolesAsync(CancellationToken cancellationToken) => repository.GetRolesAsync(cancellationToken);
    /// <inheritdoc />
    public Task<RoleResponse?> GetRoleAsync(int id, CancellationToken cancellationToken) => repository.GetRoleAsync(id, cancellationToken);
    /// <inheritdoc />
    public async Task<RoleResponse> CreateRoleAsync(UpsertRoleRequest request, CancellationToken cancellationToken)
    {
        var entity = await repository.CreateRoleAsync(request, cancellationToken);
        return (await repository.GetRoleAsync(entity.Id, cancellationToken))!;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateRoleAsync(int id, UpsertRoleRequest request, CancellationToken cancellationToken)
    {
        var employeeIds = await repository.GetEmployeeIdsForRoleAsync(id, cancellationToken);
        var updated = await repository.UpdateRoleAsync(id, request, cancellationToken);
        if (updated)
        {
            await InvalidateEmployeesAsync(employeeIds, cancellationToken);
        }

        return updated;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteRoleAsync(int id, CancellationToken cancellationToken)
    {
        var employeeIds = await repository.GetEmployeeIdsForRoleAsync(id, cancellationToken);
        var deleted = await repository.DeleteRoleAsync(id, cancellationToken);
        if (deleted)
        {
            await InvalidateEmployeesAsync(employeeIds, cancellationToken);
        }

        return deleted;
    }

    /// <inheritdoc />
    public Task<SignatureImageFileResponse?> GetSignatureAsync(int employeeId, CancellationToken cancellationToken) =>
        repository.GetSignatureAsync(employeeId, cancellationToken);

    /// <inheritdoc />
    public async Task<SignatureImageFileResponse?> CreateSignatureAsync(int employeeId, UpsertSignatureImageFileRequest request, CancellationToken cancellationToken)
    {
        var entity = await repository.CreateSignatureAsync(employeeId, request, cancellationToken);
        return entity is null ? null : await repository.GetSignatureAsync(employeeId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> UpdateSignatureAsync(int employeeId, UpsertSignatureImageFileRequest request, CancellationToken cancellationToken) =>
        repository.UpdateSignatureAsync(employeeId, request, cancellationToken);

    /// <inheritdoc />
    public Task<bool> DeleteSignatureAsync(int employeeId, CancellationToken cancellationToken) =>
        repository.DeleteSignatureAsync(employeeId, cancellationToken);

    private Task InvalidateEmployeesAsync(IReadOnlyList<int> employeeIds, CancellationToken cancellationToken) =>
        Task.WhenAll(employeeIds.Select(employeeId => cache.RemoveAsync(employeeId, cancellationToken)));
}

using Legacy.Maliev.EmployeeService.Application.Models;
using Legacy.Maliev.EmployeeService.Domain;

namespace Legacy.Maliev.EmployeeService.Application.Interfaces;

/// <summary>Employee application boundary.</summary>
public interface IEmployeeService
{
    /// <summary>Gets one employee.</summary>
    Task<EmployeeResponse?> GetEmployeeAsync(int id, CancellationToken cancellationToken);
    /// <summary>Gets a bounded employee page.</summary>
    Task<PaginatedResponse<EmployeeResponse>?> GetEmployeesAsync(EmployeeSortType? sort, string? search, int? index, int? size, CancellationToken cancellationToken);
    /// <summary>Creates an employee.</summary>
    Task<EmployeeResponse> CreateEmployeeAsync(UpsertEmployeeRequest request, CancellationToken cancellationToken);
    /// <summary>Updates an employee.</summary>
    Task<bool> UpdateEmployeeAsync(int id, UpsertEmployeeRequest request, CancellationToken cancellationToken);
    /// <summary>Deletes an employee.</summary>
    Task<bool> DeleteEmployeeAsync(int id, CancellationToken cancellationToken);
    /// <summary>Gets all employee addresses.</summary>
    Task<IReadOnlyList<AddressResponse>> GetAddressesAsync(CancellationToken cancellationToken);
    /// <summary>Gets one employee address.</summary>
    Task<AddressResponse?> GetAddressAsync(int id, CancellationToken cancellationToken);
    /// <summary>Creates an employee address.</summary>
    Task<AddressResponse> CreateAddressAsync(UpsertAddressRequest request, CancellationToken cancellationToken);
    /// <summary>Updates an employee address.</summary>
    Task<bool> UpdateAddressAsync(int id, UpsertAddressRequest request, CancellationToken cancellationToken);
    /// <summary>Deletes an employee address.</summary>
    Task<bool> DeleteAddressAsync(int id, CancellationToken cancellationToken);
    /// <summary>Gets all employee roles.</summary>
    Task<IReadOnlyList<RoleResponse>> GetRolesAsync(CancellationToken cancellationToken);
    /// <summary>Gets one employee role.</summary>
    Task<RoleResponse?> GetRoleAsync(int id, CancellationToken cancellationToken);
    /// <summary>Creates an employee role.</summary>
    Task<RoleResponse> CreateRoleAsync(UpsertRoleRequest request, CancellationToken cancellationToken);
    /// <summary>Updates an employee role.</summary>
    Task<bool> UpdateRoleAsync(int id, UpsertRoleRequest request, CancellationToken cancellationToken);
    /// <summary>Deletes an employee role.</summary>
    Task<bool> DeleteRoleAsync(int id, CancellationToken cancellationToken);
    /// <summary>Gets signature metadata for one employee.</summary>
    Task<SignatureImageFileResponse?> GetSignatureAsync(int employeeId, CancellationToken cancellationToken);
    /// <summary>Creates signature metadata for one employee.</summary>
    Task<SignatureImageFileResponse?> CreateSignatureAsync(int employeeId, UpsertSignatureImageFileRequest request, CancellationToken cancellationToken);
    /// <summary>Updates signature metadata for one employee.</summary>
    Task<bool> UpdateSignatureAsync(int employeeId, UpsertSignatureImageFileRequest request, CancellationToken cancellationToken);
    /// <summary>Deletes signature metadata for one employee.</summary>
    Task<bool> DeleteSignatureAsync(int employeeId, CancellationToken cancellationToken);
    /// <summary>Validates employee credentials through AuthService.</summary>
    Task<IdentityOperationResult> ValidateCredentialsAsync(UserValidationRequest request, CancellationToken cancellationToken);
    /// <summary>Creates an AuthService-owned employee identity.</summary>
    Task<IdentityOperationResult> CreateIdentityAsync(int employeeId, EmployeeIdentityRequest request, string? legacyPassword, CancellationToken cancellationToken);
    /// <summary>Gets an AuthService-owned employee identity.</summary>
    Task<EmployeeIdentityResponse?> GetIdentityAsync(int employeeId, CancellationToken cancellationToken);
    /// <summary>Updates an AuthService-owned employee identity.</summary>
    Task<IdentityOperationResult> UpdateIdentityAsync(int employeeId, EmployeeIdentityRequest request, CancellationToken cancellationToken);
    /// <summary>Deletes an AuthService-owned employee identity.</summary>
    Task<IdentityOperationResult> DeleteIdentityAsync(int employeeId, CancellationToken cancellationToken);
}

/// <summary>Employee PostgreSQL persistence boundary.</summary>
public interface IEmployeeRepository
{
    /// <summary>Gets one employee projection.</summary>
    Task<EmployeeResponse?> GetEmployeeAsync(int id, CancellationToken cancellationToken);
    /// <summary>Gets a bounded employee page.</summary>
    Task<PaginatedResponse<EmployeeResponse>?> GetEmployeesAsync(EmployeeSortType? sort, string? search, int pageIndex, int pageSize, CancellationToken cancellationToken);
    /// <summary>Creates an employee entity.</summary>
    Task<Employee> CreateEmployeeAsync(UpsertEmployeeRequest request, CancellationToken cancellationToken);
    /// <summary>Updates an employee entity.</summary>
    Task<bool> UpdateEmployeeAsync(int id, UpsertEmployeeRequest request, CancellationToken cancellationToken);
    /// <summary>Deletes an employee entity.</summary>
    Task<bool> DeleteEmployeeAsync(int id, CancellationToken cancellationToken);
    /// <summary>Gets address projections.</summary>
    Task<IReadOnlyList<AddressResponse>> GetAddressesAsync(CancellationToken cancellationToken);
    /// <summary>Gets an address projection.</summary>
    Task<AddressResponse?> GetAddressAsync(int id, CancellationToken cancellationToken);
    /// <summary>Creates an address entity.</summary>
    Task<Address> CreateAddressAsync(UpsertAddressRequest request, CancellationToken cancellationToken);
    /// <summary>Updates an address entity.</summary>
    Task<bool> UpdateAddressAsync(int id, UpsertAddressRequest request, CancellationToken cancellationToken);
    /// <summary>Gets employees whose cached projection references an address.</summary>
    Task<IReadOnlyList<int>> GetEmployeeIdsForAddressAsync(int id, CancellationToken cancellationToken);
    /// <summary>Deletes an address entity.</summary>
    Task<bool> DeleteAddressAsync(int id, CancellationToken cancellationToken);
    /// <summary>Gets role projections.</summary>
    Task<IReadOnlyList<RoleResponse>> GetRolesAsync(CancellationToken cancellationToken);
    /// <summary>Gets a role projection.</summary>
    Task<RoleResponse?> GetRoleAsync(int id, CancellationToken cancellationToken);
    /// <summary>Creates a role entity.</summary>
    Task<Role> CreateRoleAsync(UpsertRoleRequest request, CancellationToken cancellationToken);
    /// <summary>Updates a role entity.</summary>
    Task<bool> UpdateRoleAsync(int id, UpsertRoleRequest request, CancellationToken cancellationToken);
    /// <summary>Gets employees whose cached projection references a role.</summary>
    Task<IReadOnlyList<int>> GetEmployeeIdsForRoleAsync(int id, CancellationToken cancellationToken);
    /// <summary>Deletes a role entity.</summary>
    Task<bool> DeleteRoleAsync(int id, CancellationToken cancellationToken);
    /// <summary>Gets signature metadata by employee identifier.</summary>
    Task<SignatureImageFileResponse?> GetSignatureAsync(int employeeId, CancellationToken cancellationToken);
    /// <summary>Creates signature metadata.</summary>
    Task<SignatureImageFile?> CreateSignatureAsync(int employeeId, UpsertSignatureImageFileRequest request, CancellationToken cancellationToken);
    /// <summary>Updates signature metadata.</summary>
    Task<bool> UpdateSignatureAsync(int employeeId, UpsertSignatureImageFileRequest request, CancellationToken cancellationToken);
    /// <summary>Deletes signature metadata.</summary>
    Task<bool> DeleteSignatureAsync(int employeeId, CancellationToken cancellationToken);
}

/// <summary>Short-lived Redis cache for authorized employee reads.</summary>
public interface IEmployeeCache
{
    /// <summary>Gets a cached employee projection.</summary>
    Task<EmployeeResponse?> GetAsync(int id, CancellationToken cancellationToken);
    /// <summary>Stores a cached employee projection.</summary>
    Task SetAsync(EmployeeResponse employee, CancellationToken cancellationToken);
    /// <summary>Removes a cached employee projection.</summary>
    Task RemoveAsync(int id, CancellationToken cancellationToken);
}

/// <summary>AuthService-owned employee identity operations.</summary>
public interface IEmployeeIdentityDirectory
{
    /// <summary>Validates credentials without account enumeration.</summary>
    Task<IdentityOperationResult> ValidateCredentialsAsync(UserValidationRequest request, CancellationToken cancellationToken);
    /// <summary>Creates an employee identity.</summary>
    Task<IdentityOperationResult> CreateAsync(int employeeId, EmployeeIdentityRequest request, string? legacyPassword, CancellationToken cancellationToken);
    /// <summary>Gets an employee identity.</summary>
    Task<EmployeeIdentityResponse?> GetAsync(int employeeId, CancellationToken cancellationToken);
    /// <summary>Updates an employee identity.</summary>
    Task<IdentityOperationResult> UpdateAsync(int employeeId, EmployeeIdentityRequest request, CancellationToken cancellationToken);
    /// <summary>Deletes an employee identity.</summary>
    Task<IdentityOperationResult> DeleteAsync(int employeeId, CancellationToken cancellationToken);
}

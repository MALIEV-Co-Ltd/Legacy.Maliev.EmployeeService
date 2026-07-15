namespace Legacy.Maliev.EmployeeService.Application.Models;

/// <summary>Legacy employee response with home-address and role projections.</summary>
public sealed record EmployeeResponse(
    int Id,
    int? RoleId,
    string FirstName,
    string LastName,
    string FullName,
    string? PhoneNumber,
    string Email,
    DateTime? DateOfBirth,
    int? HomeAddressId,
    DateTime? CreatedDate,
    DateTime? ModifiedDate,
    AddressResponse? HomeAddress,
    RoleResponse? Role);

/// <summary>Legacy employee address response.</summary>
public sealed record AddressResponse(
    int Id,
    string? Building,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    int CountryId,
    DateTime? CreatedDate,
    DateTime? ModifiedDate);

/// <summary>Legacy employee-role response.</summary>
public sealed record RoleResponse(int Id, string? Name, string? Description, DateTime? CreatedDate, DateTime? ModifiedDate);

/// <summary>Legacy signature-object metadata response.</summary>
public sealed record SignatureImageFileResponse(int Id, int EmployeeId, string Bucket, string ObjectName, DateTime? CreatedDate, DateTime? ModifiedDate);

/// <summary>Employee create/update request preserving legacy property names.</summary>
public sealed record UpsertEmployeeRequest(
    int? RoleId,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    string Email,
    DateTime? DateOfBirth,
    int? HomeAddressId);

/// <summary>Employee address create/update request.</summary>
public sealed record UpsertAddressRequest(
    string? Building,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    int CountryId);

/// <summary>Employee role create/update request.</summary>
public sealed record UpsertRoleRequest(string? Name, string? Description);

/// <summary>Signature metadata create/update request.</summary>
public sealed record UpsertSignatureImageFileRequest(string Bucket, string ObjectName, int? EmployeeId = null);

/// <summary>Preserves the legacy paginated response shape.</summary>
public sealed record PaginatedResponse<T>(IReadOnlyList<T> Items, int PageIndex, int TotalPages, int TotalRecords)
{
    /// <summary>Indicates whether another page follows the current page.</summary>
    public bool HasNextPage => PageIndex < TotalPages;
    /// <summary>Indicates whether a page precedes the current page.</summary>
    public bool HasPreviousPage => PageIndex > 1;
}

/// <summary>Legacy employee sort names and numeric values.</summary>
public enum EmployeeSortType
{
    /// <summary>Employee identifier ascending.</summary>
    EmployeeId_Ascending,
    /// <summary>Employee identifier descending.</summary>
    EmployeeId_Descending,
    /// <summary>Employee email ascending.</summary>
    EmployeeEmail_Ascending,
    /// <summary>Employee email descending.</summary>
    EmployeeEmail_Descending,
}

/// <summary>Safe compatibility identity DTO; credential and recovery secrets are absent.</summary>
public sealed record EmployeeIdentityRequest(
    string? Id,
    string? UserName,
    string? Email,
    bool EmailConfirmed,
    string? PhoneNumber,
    bool PhoneNumberConfirmed,
    bool TwoFactorEnabled,
    DateTimeOffset? LockoutEnd,
    bool LockoutEnabled,
    int AccessFailedCount,
    int DatabaseID,
    string? FaxNumber,
    string? MobileNumber);

/// <summary>Safe identity response compatible with fields consumed by legacy staff screens.</summary>
public sealed record EmployeeIdentityResponse(
    string Id,
    string? UserName,
    string? Email,
    bool EmailConfirmed,
    string? PhoneNumber,
    bool PhoneNumberConfirmed,
    bool TwoFactorEnabled,
    DateTimeOffset? LockoutEnd,
    bool LockoutEnabled,
    int AccessFailedCount,
    int DatabaseID,
    string? FaxNumber,
    string? MobileNumber);

/// <summary>Non-enumerating employee credential validation request.</summary>
public sealed record UserValidationRequest(string Username, string Password);

/// <summary>Result from the AuthService employee-identity compatibility boundary.</summary>
public sealed record IdentityOperationResult(bool Succeeded, EmployeeIdentityResponse? Identity = null, IReadOnlyList<string>? Errors = null);

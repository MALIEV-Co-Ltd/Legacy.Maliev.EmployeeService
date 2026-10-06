using System.Security.Cryptography;
using System.Text.Json;

namespace Legacy.Maliev.EmployeeService.Application.Models;

/// <summary>An employee-only edit projection, excluding independently mutable address and role rows.</summary>
public sealed record EmployeeEditResponse(
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
    DateTime? ModifiedDate)
{
    /// <summary>Extracts the employee-owned fields from the ordinary read projection.</summary>
    public static EmployeeEditResponse From(EmployeeResponse employee) => new(
        employee.Id, employee.RoleId, employee.FirstName, employee.LastName, employee.FullName,
        employee.PhoneNumber, employee.Email, employee.DateOfBirth, employee.HomeAddressId,
        employee.CreatedDate, employee.ModifiedDate);
}

/// <summary>Outcome of an atomic conditional edit.</summary>
public enum EmployeeEditOutcome
{
    /// <summary>The row was updated.</summary>
    Updated,
    /// <summary>The row does not exist.</summary>
    NotFound,
    /// <summary>The supplied edit version no longer matches.</summary>
    PreconditionFailed,
}

/// <summary>An edit result with the committed version only on success.</summary>
public sealed record EmployeeEditResult(EmployeeEditOutcome Outcome, string? Version = null);

/// <summary>A commit may have succeeded; the caller must not retry or classify it as a stale edit.</summary>
public sealed class EmployeeEditUncertainException() : Exception("The edit commit outcome is uncertain");

/// <summary>Strong resource-bound versions of employee-owned edit projections.</summary>
public static class EmployeeEditVersion
{
    /// <summary>Versions only employee fields, not related address or role contents.</summary>
    public static string ForEmployee(EmployeeResponse employee) => Hash("employee", EmployeeEditResponse.From(employee));

    /// <summary>Versions one address's complete scalar projection.</summary>
    public static string ForAddress(AddressResponse address) => Hash("address", address);

    /// <summary>Accepts exactly one quoted strong version emitted by this service.</summary>
    public static bool IsValid(string value) => value.Length == 66 && value[0] == '"' && value[^1] == '"' &&
        value.AsSpan(1, 64).IndexOfAnyExcept("0123456789abcdef".AsSpan()) < 0;

    private static string Hash<T>(string kind, T projection) =>
        '"' + Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { kind, projection }))) + '"';
}

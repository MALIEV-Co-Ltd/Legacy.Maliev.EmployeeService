using Legacy.Maliev.EmployeeService.Api.Authorization;
using Legacy.Maliev.EmployeeService.Application.Interfaces;
using Legacy.Maliev.EmployeeService.Application.Models;
using Maliev.Aspire.ServiceDefaults.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Legacy.Maliev.EmployeeService.Api.Controllers;

/// <summary>Preserves legacy employee profile routes without taking ownership of staff identities.</summary>
[ApiController]
[Route("[controller]")]
[Authorize]
public sealed class EmployeesController(IEmployeeService service) : ControllerBase
{
    /// <summary>Creates an employee.</summary>
    [HttpPost]
    [RequirePermission(EmployeePermissions.EmployeesCreate)]
    public async Task<IActionResult> CreateEmployeeAsync(UpsertEmployeeRequest item, CancellationToken cancellationToken)
    {
        if (!Valid(item)) return BadRequest("Employee data is required");
        var employee = await service.CreateEmployeeAsync(item, cancellationToken);
        return CreatedAtRoute("GetEmployee", new { employeeId = employee.Id }, employee);
    }

    /// <summary>Deletes an employee.</summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(EmployeePermissions.EmployeesDelete, ResourcePathTemplate = "/employees/{id}", RequireLiveCheck = true, IsCritical = true)]
    public async Task<IActionResult> DeleteEmployeeAsync(int id, CancellationToken cancellationToken) =>
        await service.DeleteEmployeeAsync(id, cancellationToken) ? NoContent() : NotFound();

    /// <summary>Gets an employee by legacy identifier.</summary>
    [HttpGet("{employeeId:int}", Name = "GetEmployee")]
    [RequirePermission(EmployeePermissions.EmployeesRead, ResourcePathTemplate = "/employees/{employeeId}")]
    public async Task<ActionResult<EmployeeResponse>> GetEmployeeAsync(int employeeId, CancellationToken cancellationToken)
    {
        var employee = await service.GetEmployeeAsync(employeeId, cancellationToken);
        return employee is null ? NotFound() : employee;
    }

    /// <summary>Gets employee-owned edit fields and a strong conditional-edit version.</summary>
    [HttpGet("{employeeId:int}/edit")]
    [RequirePermission(EmployeePermissions.EmployeesRead, ResourcePathTemplate = "/employees/{employeeId}")]
    public async Task<ActionResult<EmployeeEditResponse>> GetEmployeeEditAsync(int employeeId, CancellationToken cancellationToken)
    {
        var employee = await service.GetEmployeeAsync(employeeId, cancellationToken);
        if (employee is null) return NotFound();
        Response.Headers.ETag = EmployeeEditVersion.ForEmployee(employee);
        Response.Headers.CacheControl = "no-store";
        return EmployeeEditResponse.From(employee);
    }

    /// <summary>Gets a bounded employee page.</summary>
    /// <param name="sort" example="EmployeeId_Ascending">The legacy employee sort value, supplied by its existing name or numeric value.</param>
    /// <param name="search">Text to search in the employee directory fields.</param>
    /// <param name="index">The one-based page index; omitted values default to 1 and values below 1 use 1.</param>
    /// <param name="size">The page size; omitted values default to 50 and supplied values are bounded from 1 to 250.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet]
    [RequirePermission(EmployeePermissions.EmployeesList)]
    public async Task<ActionResult<PaginatedResponse<EmployeeResponse>>> GetPaginatedAsync(
        [FromQuery] EmployeeSortType? sort,
        [FromQuery] string? search,
        [FromQuery] int? index,
        [FromQuery] int? size,
        CancellationToken cancellationToken)
    {
        var employees = await service.GetEmployeesAsync(sort, search, index, size, cancellationToken);
        return employees is null ? NotFound() : employees;
    }

    /// <summary>Updates an employee.</summary>
    [HttpPut("{id:int}")]
    [RequirePermission(EmployeePermissions.EmployeesUpdate, ResourcePathTemplate = "/employees/{id}")]
    public async Task<ActionResult> UpdateEmployeeAsync(int id, UpsertEmployeeRequest item, CancellationToken cancellationToken)
    {
        if (!Valid(item)) return BadRequest();
        return await service.UpdateEmployeeAsync(id, item, cancellationToken) ? NoContent() : NotFound();
    }

    /// <summary>Updates employee-owned fields only when the original edit version still matches.</summary>
    [HttpPut("{id:int}/versioned")]
    [RequirePermission(EmployeePermissions.EmployeesUpdate, ResourcePathTemplate = "/employees/{id}")]
    public async Task<ActionResult> UpdateEmployeeVersionedAsync(int id, UpsertEmployeeRequest item, CancellationToken cancellationToken)
    {
        if (!Valid(item)) return BadRequest();
        if (!EmployeeEditPrecondition.TryRead(Request, out var version)) return BadRequest();
        if (version is null) return StatusCode(StatusCodes.Status428PreconditionRequired);
        var result = await service.UpdateEmployeeIfMatchAsync(id, item, version, cancellationToken);
        if (result.Outcome == EmployeeEditOutcome.NotFound) return NotFound();
        if (result.Outcome == EmployeeEditOutcome.PreconditionFailed)
            return StatusCode(StatusCodes.Status412PreconditionFailed);
        Response.Headers.ETag = result.Version;
        return NoContent();
    }

    /// <summary>Updates the authenticated employee's own non-administrative profile fields.</summary>
    [HttpPut("{employeeId:int}/profile")]
    [RequirePermission(EmployeePermissions.EmployeesSelfUpdate, ResourcePathTemplate = "/employees/{employeeId}/profile")]
    public async Task<IActionResult> UpdateSelfProfileAsync(
        int employeeId,
        UpdateEmployeeSelfProfileRequest item,
        CancellationToken cancellationToken)
    {
        if (!Valid(item))
        {
            return BadRequest();
        }

        return await service.UpdateSelfProfileAsync(employeeId, item, cancellationToken) ? NoContent() : NotFound();
    }

    private static bool Valid(UpsertEmployeeRequest request) =>
        ValidAdministrativeName(request.FirstName) && ValidAdministrativeName(request.LastName) &&
        !string.IsNullOrWhiteSpace(request.Email) &&
        request.Email.Contains('@', StringComparison.Ordinal);

    // Preserve the original administrative SQL Server nvarchar(256) UTF-16 capacity.
    // Refuse excess trailing spaces without normalization or truncation.
    private static bool ValidAdministrativeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256) return false;
        var remaining = value.AsSpan();
        while (!remaining.IsEmpty)
        {
            if (System.Text.Rune.DecodeFromUtf16(remaining, out var rune, out var consumed) !=
                System.Buffers.OperationStatus.Done || rune.Value == 0)
            {
                return false;
            }

            remaining = remaining[consumed..];
        }

        return true;
    }

    private static bool Valid(UpdateEmployeeSelfProfileRequest request) =>
        !string.IsNullOrWhiteSpace(request.FirstName) &&
        request.FirstName.Length <= 256 &&
        !string.IsNullOrWhiteSpace(request.LastName) &&
        request.LastName.Length <= 256 &&
        (request.PhoneNumber is null || request.PhoneNumber.Length <= 256);
}

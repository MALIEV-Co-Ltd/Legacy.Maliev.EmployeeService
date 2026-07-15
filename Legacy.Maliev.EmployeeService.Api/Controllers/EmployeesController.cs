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

    /// <summary>Gets a bounded employee page.</summary>
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

    private static bool Valid(UpsertEmployeeRequest request) =>
        !string.IsNullOrWhiteSpace(request.FirstName) &&
        !string.IsNullOrWhiteSpace(request.LastName) &&
        !string.IsNullOrWhiteSpace(request.Email) &&
        request.Email.Contains('@', StringComparison.Ordinal);
}

using Legacy.Maliev.EmployeeService.Api.Authorization;
using Legacy.Maliev.EmployeeService.Application.Interfaces;
using Legacy.Maliev.EmployeeService.Application.Models;
using Maliev.Aspire.ServiceDefaults.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Legacy.Maliev.EmployeeService.Api.Controllers;

/// <summary>Preserves legacy employee routes while AuthService owns staff identities.</summary>
[ApiController]
[Route("[controller]")]
[Authorize]
public sealed class EmployeesController(IEmployeeService service) : ControllerBase
{
    /// <summary>Validates staff credentials without account enumeration.</summary>
    [HttpPost("v1/validate")]
    [RequirePermission(EmployeePermissions.CredentialsValidate, RequireLiveCheck = true, IsCritical = true, AuditPurpose = "Legacy employee credential validation")]
    public async Task<IActionResult> ValidateUserCredentialsAsync(UserValidationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest("Username and password are required.");
        }

        return (await service.ValidateCredentialsAsync(request, cancellationToken)).Succeeded ? Ok() : Unauthorized();
    }

    /// <summary>Creates an employee.</summary>
    [HttpPost]
    [RequirePermission(EmployeePermissions.EmployeesCreate, RequireLiveCheck = true)]
    public async Task<IActionResult> CreateEmployeeAsync(UpsertEmployeeRequest item, CancellationToken cancellationToken)
    {
        if (!Valid(item)) return BadRequest("Employee data is required");
        var employee = await service.CreateEmployeeAsync(item, cancellationToken);
        return CreatedAtRoute("GetEmployee", new { employeeId = employee.Id }, employee);
    }

    /// <summary>Creates an AuthService-owned employee identity.</summary>
    [HttpPost("{id:int}/identity/{password?}")]
    [RequirePermission(EmployeePermissions.IdentitiesManage, ResourcePathTemplate = "/employees/{id}/identity", RequireLiveCheck = true, IsCritical = true)]
    public async Task<IActionResult> CreateIdentityAsync(int id, EmployeeIdentityRequest item, string? password, CancellationToken cancellationToken)
    {
        var result = await service.CreateIdentityAsync(id, item, password, cancellationToken);
        if (!result.Succeeded) return result.Errors?.Contains("Employee not found") == true ? NotFound() : BadRequest(result.Errors);
        return CreatedAtRoute("GetIdentity", new { id }, result.Identity);
    }

    /// <summary>Deletes an employee.</summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(EmployeePermissions.EmployeesDelete, ResourcePathTemplate = "/employees/{id}", RequireLiveCheck = true, IsCritical = true)]
    public async Task<IActionResult> DeleteEmployeeAsync(int id, CancellationToken cancellationToken) =>
        await service.DeleteEmployeeAsync(id, cancellationToken) ? NoContent() : NotFound();

    /// <summary>Deletes an AuthService-owned employee identity.</summary>
    [HttpDelete("{id:int}/identity")]
    [RequirePermission(EmployeePermissions.IdentitiesManage, ResourcePathTemplate = "/employees/{id}/identity", RequireLiveCheck = true, IsCritical = true)]
    public async Task<ActionResult> DeleteIdentityAsync(int id, CancellationToken cancellationToken)
    {
        var result = await service.DeleteIdentityAsync(id, cancellationToken);
        return result.Succeeded ? NoContent() : BadRequest(result.Errors);
    }

    /// <summary>Gets an employee by legacy identifier.</summary>
    [HttpGet("{employeeId:int}", Name = "GetEmployee")]
    [RequirePermission(EmployeePermissions.EmployeesRead, ResourcePathTemplate = "/employees/{employeeId}", RequireLiveCheck = true)]
    public async Task<ActionResult<EmployeeResponse>> GetEmployeeAsync(int employeeId, CancellationToken cancellationToken)
    {
        var employee = await service.GetEmployeeAsync(employeeId, cancellationToken);
        return employee is null ? NotFound() : employee;
    }

    /// <summary>Gets safe employee identity fields.</summary>
    [HttpGet("{id:int}/identity", Name = "GetIdentity")]
    [RequirePermission(EmployeePermissions.IdentitiesRead, ResourcePathTemplate = "/employees/{id}/identity", RequireLiveCheck = true)]
    public async Task<ActionResult<EmployeeIdentityResponse>> GetIdentityAsync(int id, CancellationToken cancellationToken)
    {
        var identity = await service.GetIdentityAsync(id, cancellationToken);
        return identity is null ? NotFound() : identity;
    }

    /// <summary>Gets a bounded employee page.</summary>
    [HttpGet]
    [RequirePermission(EmployeePermissions.EmployeesList, RequireLiveCheck = true)]
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
    [RequirePermission(EmployeePermissions.EmployeesUpdate, ResourcePathTemplate = "/employees/{id}", RequireLiveCheck = true)]
    public async Task<ActionResult> UpdateEmployeeAsync(int id, UpsertEmployeeRequest item, CancellationToken cancellationToken)
    {
        if (!Valid(item)) return BadRequest();
        return await service.UpdateEmployeeAsync(id, item, cancellationToken) ? NoContent() : NotFound();
    }

    /// <summary>Updates safe employee identity fields.</summary>
    [HttpPut("{id:int}/identity")]
    [RequirePermission(EmployeePermissions.IdentitiesManage, ResourcePathTemplate = "/employees/{id}/identity", RequireLiveCheck = true, IsCritical = true)]
    public async Task<ActionResult> UpdateIdentityAsync(int id, EmployeeIdentityRequest item, CancellationToken cancellationToken)
    {
        var result = await service.UpdateIdentityAsync(id, item, cancellationToken);
        return result.Succeeded ? NoContent() : BadRequest(result.Errors);
    }

    private static bool Valid(UpsertEmployeeRequest request) =>
        !string.IsNullOrWhiteSpace(request.FirstName) &&
        !string.IsNullOrWhiteSpace(request.LastName) &&
        !string.IsNullOrWhiteSpace(request.Email) &&
        request.Email.Contains('@', StringComparison.Ordinal);
}

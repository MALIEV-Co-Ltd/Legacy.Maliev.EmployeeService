using Legacy.Maliev.EmployeeService.Api.Authorization;
using Legacy.Maliev.EmployeeService.Application.Interfaces;
using Legacy.Maliev.EmployeeService.Application.Models;
using Maliev.Aspire.ServiceDefaults.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Legacy.Maliev.EmployeeService.Api.Controllers;

/// <summary>Legacy employee-role CRUD routes.</summary>
[ApiController]
[Route("employees/[controller]")]
[Authorize]
public sealed class RolesController(IEmployeeService service) : ControllerBase
{
    /// <summary>Creates an employee role.</summary>
    [HttpPost]
    [RequirePermission(EmployeePermissions.RolesCreate, RequireLiveCheck = true)]
    public async Task<ActionResult> CreateRoleAsync(UpsertRoleRequest item, CancellationToken cancellationToken)
    {
        var role = await service.CreateRoleAsync(item, cancellationToken);
        return CreatedAtRoute("GetRole", new { roleId = role.Id }, role);
    }

    /// <summary>Deletes an employee role.</summary>
    [HttpDelete("{roleId:int}")]
    [RequirePermission(EmployeePermissions.RolesDelete, ResourcePathTemplate = "/employees/roles/{roleId}", RequireLiveCheck = true, IsCritical = true)]
    public async Task<ActionResult> DeleteRoleAsync(int roleId, CancellationToken cancellationToken) =>
        await service.DeleteRoleAsync(roleId, cancellationToken) ? NoContent() : NotFound();

    /// <summary>Gets one employee role.</summary>
    [HttpGet("{roleId:int}", Name = "GetRole")]
    [RequirePermission(EmployeePermissions.RolesRead, RequireLiveCheck = true)]
    public async Task<ActionResult<RoleResponse>> GetRoleAsync(int roleId, CancellationToken cancellationToken)
    {
        var role = await service.GetRoleAsync(roleId, cancellationToken);
        return role is null ? NotFound() : role;
    }

    /// <summary>Gets all employee roles.</summary>
    [HttpGet]
    [RequirePermission(EmployeePermissions.RolesRead, RequireLiveCheck = true)]
    public async Task<ActionResult<IReadOnlyList<RoleResponse>>> GetRolesAsync(CancellationToken cancellationToken)
    {
        var roles = await service.GetRolesAsync(cancellationToken);
        return roles.Count == 0 ? NotFound() : Ok(roles);
    }

    /// <summary>Updates an employee role.</summary>
    [HttpPut("{roleId:int}")]
    [RequirePermission(EmployeePermissions.RolesUpdate, ResourcePathTemplate = "/employees/roles/{roleId}", RequireLiveCheck = true)]
    public async Task<ActionResult> UpdateRoleAsync(int roleId, UpsertRoleRequest item, CancellationToken cancellationToken) =>
        await service.UpdateRoleAsync(roleId, item, cancellationToken) ? NoContent() : NotFound();
}

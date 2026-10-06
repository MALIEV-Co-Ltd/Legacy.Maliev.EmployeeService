using Legacy.Maliev.EmployeeService.Api.Authorization;
using Legacy.Maliev.EmployeeService.Application.Interfaces;
using Legacy.Maliev.EmployeeService.Application.Models;
using Maliev.Aspire.ServiceDefaults.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Legacy.Maliev.EmployeeService.Api.Controllers;

/// <summary>Edits the captured employee home address while atomically fencing its binding.</summary>
[ApiController]
[Authorize]
[Route("employees/{employeeId:int}/home-address")]
[RequirePermission(EmployeePermissions.EmployeesUpdate, ResourcePathTemplate = "/employees/{employeeId}")]
public sealed class EmployeeHomeAddressController(IEmployeeService service, IAuthorizationService authorization) : ControllerBase
{
    /// <summary>Requires both captured strong versions and permission for the captured address.</summary>
    [HttpPut("versioned")]
    public async Task<IActionResult> UpdateAsync(int employeeId, EmployeeHomeAddressEditRequest request, CancellationToken cancellationToken)
    {
        var addressPermission = new PermissionRequirement(EmployeePermissions.AddressesUpdate,
            $"/employees/addresses/{request.AddressId}");
        if (!(await authorization.AuthorizeAsync(User, HttpContext, [addressPermission])).Succeeded) return Forbid();
        if (!EmployeeEditPrecondition.TryRead(Request, out var addressVersion) ||
            !EmployeeEditPrecondition.TryRead(Request, "X-Employee-If-Match", out var employeeVersion)) return BadRequest();
        if (addressVersion is null || employeeVersion is null) return StatusCode(StatusCodes.Status428PreconditionRequired);
        var result = await service.UpdateHomeAddressIfMatchAsync(employeeId, request, employeeVersion, addressVersion, cancellationToken);
        if (result.Outcome == EmployeeEditOutcome.NotFound) return NotFound();
        if (result.Outcome == EmployeeEditOutcome.PreconditionFailed) return StatusCode(StatusCodes.Status412PreconditionFailed);
        Response.Headers.ETag = result.AddressVersion;
        Response.Headers["X-Employee-ETag"] = result.EmployeeVersion;
        return NoContent();
    }
}

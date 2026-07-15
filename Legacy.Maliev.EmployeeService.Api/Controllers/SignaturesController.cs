using Legacy.Maliev.EmployeeService.Api.Authorization;
using Legacy.Maliev.EmployeeService.Application.Interfaces;
using Legacy.Maliev.EmployeeService.Application.Models;
using Maliev.Aspire.ServiceDefaults.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Legacy.Maliev.EmployeeService.Api.Controllers;

/// <summary>Legacy employee signature-object metadata routes.</summary>
[ApiController]
[Route("employees/[controller]")]
[Authorize]
public sealed class SignaturesController(IEmployeeService service) : ControllerBase
{
    /// <summary>Creates signature object metadata for an employee.</summary>
    [HttpPost("/employees/{employeeId:int}/[controller]")]
    [RequirePermission(EmployeePermissions.SignaturesWrite, ResourcePathTemplate = "/employees/{employeeId}", RequireLiveCheck = true)]
    public async Task<ActionResult> CreateSignatureImageFileEntryAsync(int employeeId, [FromQuery] string bucket, [FromQuery] string objectName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(bucket) || string.IsNullOrWhiteSpace(objectName)) return BadRequest();
        var signature = await service.CreateSignatureAsync(employeeId, new UpsertSignatureImageFileRequest(bucket, objectName), cancellationToken);
        return signature is null ? NotFound("Employee not found") : CreatedAtRoute("GetEmployeeSignatureImageFile", new { employeeId }, signature);
    }

    /// <summary>Deletes signature object metadata for an employee.</summary>
    [HttpDelete("{employeeId:int}")]
    [RequirePermission(EmployeePermissions.SignaturesDelete, ResourcePathTemplate = "/employees/{employeeId}/signature", RequireLiveCheck = true, IsCritical = true)]
    public async Task<ActionResult> DeleteSignatureImageFileAsync(int employeeId, CancellationToken cancellationToken) =>
        await service.DeleteSignatureAsync(employeeId, cancellationToken) ? NoContent() : NotFound();

    /// <summary>Gets signature object metadata for an employee.</summary>
    [HttpGet("{employeeId:int}", Name = "GetEmployeeSignatureImageFile")]
    [RequirePermission(EmployeePermissions.SignaturesRead, ResourcePathTemplate = "/employees/{employeeId}/signature", RequireLiveCheck = true)]
    public async Task<ActionResult<SignatureImageFileResponse>> GetSignatureImageFileAsync(int employeeId, CancellationToken cancellationToken)
    {
        var signature = await service.GetSignatureAsync(employeeId, cancellationToken);
        return signature is null ? NotFound() : signature;
    }

    /// <summary>Updates signature object metadata for an employee.</summary>
    [HttpPut("{employeeId:int}")]
    [RequirePermission(EmployeePermissions.SignaturesWrite, ResourcePathTemplate = "/employees/{employeeId}/signature", RequireLiveCheck = true)]
    public async Task<ActionResult> UpdateSignatureImageFileAsync(int employeeId, UpsertSignatureImageFileRequest item, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(item.Bucket) || string.IsNullOrWhiteSpace(item.ObjectName)) return BadRequest();
        return await service.UpdateSignatureAsync(employeeId, item, cancellationToken) ? NoContent() : NotFound();
    }
}

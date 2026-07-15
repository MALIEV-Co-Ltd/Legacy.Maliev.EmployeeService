using Legacy.Maliev.EmployeeService.Api.Authorization;
using Legacy.Maliev.EmployeeService.Application.Interfaces;
using Legacy.Maliev.EmployeeService.Application.Models;
using Maliev.Aspire.ServiceDefaults.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Legacy.Maliev.EmployeeService.Api.Controllers;

/// <summary>Legacy employee-address CRUD routes.</summary>
[ApiController]
[Route("employees/[controller]")]
[Authorize]
public sealed class AddressesController(IEmployeeService service) : ControllerBase
{
    /// <summary>Creates an employee address.</summary>
    [HttpPost]
    [RequirePermission(EmployeePermissions.AddressesCreate, RequireLiveCheck = true)]
    public async Task<ActionResult> CreateAddressAsync(UpsertAddressRequest item, CancellationToken cancellationToken)
    {
        var address = await service.CreateAddressAsync(item, cancellationToken);
        return CreatedAtRoute("GetAddress", new { addressId = address.Id }, address);
    }

    /// <summary>Deletes an employee address.</summary>
    [HttpDelete("{addressId:int}")]
    [RequirePermission(EmployeePermissions.AddressesDelete, ResourcePathTemplate = "/employees/addresses/{addressId}", RequireLiveCheck = true)]
    public async Task<ActionResult> DeleteAddressAsync(int addressId, CancellationToken cancellationToken) =>
        await service.DeleteAddressAsync(addressId, cancellationToken) ? NoContent() : NotFound();

    /// <summary>Gets one employee address.</summary>
    [HttpGet("{addressId:int}", Name = "GetAddress")]
    [RequirePermission(EmployeePermissions.AddressesRead, ResourcePathTemplate = "/employees/addresses/{addressId}", RequireLiveCheck = true)]
    public async Task<ActionResult<AddressResponse>> GetAddressAsync(int addressId, CancellationToken cancellationToken)
    {
        var address = await service.GetAddressAsync(addressId, cancellationToken);
        return address is null ? NotFound() : address;
    }

    /// <summary>Gets all employee addresses.</summary>
    [HttpGet]
    [RequirePermission(EmployeePermissions.AddressesList, RequireLiveCheck = true)]
    public async Task<ActionResult<IReadOnlyList<AddressResponse>>> GetAddressesAsync(CancellationToken cancellationToken)
    {
        var addresses = await service.GetAddressesAsync(cancellationToken);
        return addresses.Count == 0 ? NotFound() : Ok(addresses);
    }

    /// <summary>Updates an employee address.</summary>
    [HttpPut("{addressId:int}")]
    [RequirePermission(EmployeePermissions.AddressesUpdate, ResourcePathTemplate = "/employees/addresses/{addressId}", RequireLiveCheck = true)]
    public async Task<ActionResult> UpdateAddressAsync(int addressId, UpsertAddressRequest item, CancellationToken cancellationToken) =>
        await service.UpdateAddressAsync(addressId, item, cancellationToken) ? NoContent() : NotFound();
}

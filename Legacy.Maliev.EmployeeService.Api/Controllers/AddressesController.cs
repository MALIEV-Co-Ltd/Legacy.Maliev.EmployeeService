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
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <param name="item">The address fields and existing country identifier to store.</param>
    /// <response code="201">The created employee address.</response>
    [HttpPost]
    [RequirePermission(EmployeePermissions.AddressesCreate)]
    [ProducesResponseType<AddressResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult> CreateAddressAsync(UpsertAddressRequest item, CancellationToken cancellationToken)
    {
        var address = await service.CreateAddressAsync(item, cancellationToken);
        return CreatedAtRoute("GetAddress", new { addressId = address.Id }, address);
    }

    /// <summary>Deletes an employee address.</summary>
    /// <remarks>Deletion requires a fresh authorization decision for this address; cached permission claims do not authorize this critical operation.</remarks>
    /// <param name="addressId">The employee-address identifier.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <response code="204">The employee address was deleted.</response>
    /// <response code="404">The employee address does not exist.</response>
    [HttpDelete("{addressId:int}")]
    [RequirePermission(EmployeePermissions.AddressesDelete, ResourcePathTemplate = "/employees/addresses/{addressId}", RequireLiveCheck = true, IsCritical = true)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteAddressAsync(int addressId, CancellationToken cancellationToken) =>
        await service.DeleteAddressAsync(addressId, cancellationToken) ? NoContent() : NotFound();

    /// <summary>Gets one employee address.</summary>
    /// <param name="addressId">The employee-address identifier.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <response code="200">The requested employee address.</response>
    /// <response code="404">The employee address does not exist.</response>
    [HttpGet("{addressId:int}", Name = "GetAddress")]
    [RequirePermission(EmployeePermissions.AddressesRead, ResourcePathTemplate = "/employees/addresses/{addressId}")]
    [ProducesResponseType<AddressResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AddressResponse>> GetAddressAsync(int addressId, CancellationToken cancellationToken)
    {
        var address = await service.GetAddressAsync(addressId, cancellationToken);
        return address is null ? NotFound() : address;
    }

    /// <summary>Gets all employee addresses.</summary>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <response code="200">The stored employee addresses.</response>
    /// <response code="404">No employee addresses exist.</response>
    [HttpGet]
    [RequirePermission(EmployeePermissions.AddressesList)]
    [ProducesResponseType<IReadOnlyList<AddressResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<AddressResponse>>> GetAddressesAsync(CancellationToken cancellationToken)
    {
        var addresses = await service.GetAddressesAsync(cancellationToken);
        return addresses.Count == 0 ? NotFound() : Ok(addresses);
    }

    /// <summary>Updates an employee address.</summary>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <param name="addressId">The employee-address identifier.</param>
    /// <param name="item">The replacement address fields and existing country identifier.</param>
    /// <response code="204">The employee address was updated.</response>
    /// <response code="404">The employee address does not exist.</response>
    [HttpPut("{addressId:int}")]
    [RequirePermission(EmployeePermissions.AddressesUpdate, ResourcePathTemplate = "/employees/addresses/{addressId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> UpdateAddressAsync(int addressId, UpsertAddressRequest item, CancellationToken cancellationToken) =>
        await service.UpdateAddressAsync(addressId, item, cancellationToken) ? NoContent() : NotFound();
}

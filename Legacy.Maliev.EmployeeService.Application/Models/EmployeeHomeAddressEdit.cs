using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Legacy.Maliev.EmployeeService.Application.Models;

/// <summary>Address fields plus the captured binding identifier; no profile or identity fields.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EmployeeHomeAddressEditRequest(
    [Range(1, int.MaxValue)] int AddressId,
    string? Building,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    int CountryId)
{
    /// <summary>Preserves the existing address field contract.</summary>
    public UpsertAddressRequest Address() => new(Building, AddressLine1, AddressLine2, City, State, PostalCode, CountryId);
}

/// <summary>A conditional bound-address edit and the affected shared projections.</summary>
public sealed record EmployeeHomeAddressEditResult(
    EmployeeEditOutcome Outcome, string? AddressVersion = null, string? EmployeeVersion = null,
    IReadOnlyList<int>? AffectedEmployeeIds = null);

/// <summary>A potentially committed address edit that must not be automatically replayed.</summary>
public sealed class EmployeeHomeAddressUncertainException(IReadOnlyList<int> affectedEmployeeIds)
    : Exception("The home-address edit commit outcome is uncertain")
{
    /// <summary>Potentially stale shared projections.</summary>
    public IReadOnlyList<int> AffectedEmployeeIds { get; } = affectedEmployeeIds;
}

/// <summary>Preserves caller cancellation while retaining independent cache cleanup information.</summary>
public sealed class EmployeeHomeAddressCanceledException(IReadOnlyList<int> affectedEmployeeIds, CancellationToken cancellationToken)
    : OperationCanceledException(cancellationToken)
{
    /// <summary>Potentially stale shared projections.</summary>
    public IReadOnlyList<int> AffectedEmployeeIds { get; } = affectedEmployeeIds;
}

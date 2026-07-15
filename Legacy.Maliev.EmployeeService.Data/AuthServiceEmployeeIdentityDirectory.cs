using System.Net.Http.Json;
using Legacy.Maliev.EmployeeService.Application.Interfaces;
using Legacy.Maliev.EmployeeService.Application.Models;

namespace Legacy.Maliev.EmployeeService.Data;

/// <summary>
/// Provides the HTTP boundary for employee identity operations owned by AuthService.
/// </summary>
/// <param name="client">The HTTP client configured for the AuthService identity API.</param>
public sealed class AuthServiceEmployeeIdentityDirectory(HttpClient client) : IEmployeeIdentityDirectory
{
    /// <inheritdoc />
    public async Task<IdentityOperationResult> ValidateCredentialsAsync(UserValidationRequest request, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync("validate", request, cancellationToken);
        return response.IsSuccessStatusCode
            ? new IdentityOperationResult(true)
            : new IdentityOperationResult(false, Errors: ["Invalid username or password"]);
    }

    /// <inheritdoc />
    public async Task<IdentityOperationResult> CreateAsync(int employeeId, EmployeeIdentityRequest request, string? legacyPassword, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(employeeId.ToString(System.Globalization.CultureInfo.InvariantCulture), new IdentityCreateEnvelope(request, legacyPassword), cancellationToken);
        return await ReadResultAsync(response, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<EmployeeIdentityResponse?> GetAsync(int employeeId, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(employeeId.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<EmployeeIdentityResponse>(cancellationToken)
            : null;
    }

    /// <inheritdoc />
    public async Task<IdentityOperationResult> UpdateAsync(int employeeId, EmployeeIdentityRequest request, CancellationToken cancellationToken)
    {
        using var response = await client.PutAsJsonAsync(employeeId.ToString(System.Globalization.CultureInfo.InvariantCulture), request, cancellationToken);
        return await ReadResultAsync(response, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IdentityOperationResult> DeleteAsync(int employeeId, CancellationToken cancellationToken)
    {
        using var response = await client.DeleteAsync(employeeId.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken);
        return await ReadResultAsync(response, cancellationToken);
    }

    private static async Task<IdentityOperationResult> ReadResultAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            var identity = response.Content.Headers.ContentLength > 0
                ? await response.Content.ReadFromJsonAsync<EmployeeIdentityResponse>(cancellationToken)
                : null;
            return new IdentityOperationResult(true, identity);
        }

        return new IdentityOperationResult(false, Errors: ["AuthService rejected the identity operation"]);
    }

    private sealed record IdentityCreateEnvelope(EmployeeIdentityRequest Identity, string? Password);
}

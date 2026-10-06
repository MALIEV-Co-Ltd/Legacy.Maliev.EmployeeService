using Legacy.Maliev.EmployeeService.Application.Models;

namespace Legacy.Maliev.EmployeeService.Api.Controllers;

internal static class EmployeeEditPrecondition
{
    internal static bool TryRead(HttpRequest request, out string? version)
        => TryRead(request, "If-Match", out version);

    internal static bool TryRead(HttpRequest request, string header, out string? version)
    {
        version = null;
        if (!request.Headers.TryGetValue(header, out var values)) return true;
        if (values.Count != 1) return false;
        var candidate = values[0];
        if (candidate is null || !EmployeeEditVersion.IsValid(candidate)) return false;
        version = candidate;
        return true;
    }
}

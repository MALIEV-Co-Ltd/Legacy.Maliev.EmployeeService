using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Legacy.Maliev.EmployeeService.Data;

/// <summary>Creates the context for explicit design-time migration commands.</summary>
public sealed class EmployeeDbContextFactory : IDesignTimeDbContextFactory<EmployeeDbContext>
{
    /// <inheritdoc />
    public EmployeeDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__EmployeeDbContext");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings__EmployeeDbContext is required for design-time migration commands.");
        }

        var options = new DbContextOptionsBuilder<EmployeeDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new EmployeeDbContext(options);
    }
}

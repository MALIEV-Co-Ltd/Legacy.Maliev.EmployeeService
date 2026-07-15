using Legacy.Maliev.EmployeeService.Application.Models;
using Legacy.Maliev.EmployeeService.Data;
using Legacy.Maliev.EmployeeService.Domain;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Legacy.Maliev.EmployeeService.Tests.Data;

public sealed class EmployeePostgresMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();

    public Task InitializeAsync() => postgres.StartAsync();

    public Task DisposeAsync() => postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task InitialMigration_PreservesEmployeeRelationsSignatureMetadataAndComputedName()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();

        var address = new Address { AddressLine1 = "1 Legacy Road", CountryId = 764 };
        var role = new Role { Name = "Director", Description = "Director" };
        dbContext.AddRange(address, role);
        await dbContext.SaveChangesAsync();
        var employee = new Employee
        {
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = "ada@example.com",
            HomeAddressId = address.Id,
            RoleId = role.Id,
        };
        dbContext.Add(employee);
        await dbContext.SaveChangesAsync();
        dbContext.Add(new SignatureImageFile { EmployeeId = employee.Id, Bucket = "legacy-signatures", ObjectName = "employees/ada.png" });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var repository = new EmployeeRepository(dbContext, TimeProvider.System);
        var loaded = await repository.GetEmployeeAsync(employee.Id, CancellationToken.None);
        var signature = await repository.GetSignatureAsync(employee.Id, CancellationToken.None);
        var addressOwners = await repository.GetEmployeeIdsForAddressAsync(address.Id, CancellationToken.None);
        var roleOwners = await repository.GetEmployeeIdsForRoleAsync(role.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal("Ada Lovelace", loaded.FullName);
        Assert.Equal(address.Id, loaded.HomeAddressId);
        Assert.Equal(role.Id, loaded.RoleId);
        Assert.Equal("employees/ada.png", signature?.ObjectName);
        Assert.Equal([employee.Id], addressOwners);
        Assert.Equal([employee.Id], roleOwners);
        Assert.Equal(4, await dbContext.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*)::int AS \"Value\" FROM information_schema.tables WHERE table_schema = 'public' AND table_name IN ('Address', 'Employee', 'Role', 'SignatureImageFile')")
            .SingleAsync());
    }

    private EmployeeDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<EmployeeDbContext>().UseNpgsql(postgres.GetConnectionString()).Options;
        return new EmployeeDbContext(options);
    }
}

using Legacy.Maliev.EmployeeService.Application.Models;
using Legacy.Maliev.EmployeeService.Data;
using Legacy.Maliev.EmployeeService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace Legacy.Maliev.EmployeeService.Tests.Data;

/// <summary>Owns disposable PostgreSQL tests for self-profile persistence and server-owned employee fields.</summary>
public sealed class EmployeeSelfProfilePostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();

    /// <summary>Starts the PostgreSQL container owned by the self-profile test instance.</summary>
    /// <returns>A task that completes when the owned PostgreSQL container has started.</returns>
    public Task InitializeAsync() => postgres.StartAsync();

    /// <summary>Disposes the PostgreSQL container owned by the self-profile test instance.</summary>
    /// <returns>A task that completes when the owned PostgreSQL container has been disposed.</returns>
    public Task DisposeAsync() => postgres.DisposeAsync().AsTask();

    /// <summary>Checks allowed self-profile persistence, name trimming, server timestamps, and preservation of administrative employee fields.</summary>
    /// <returns>A task that completes after verifying allowed self-profile persistence, name trimming, server timestamps, and preservation of administrative employee fields.</returns>
    [Fact]
    public async Task UpdateSelfProfileAsync_PersistsAllowedFieldsAndPreservesServerOwnedFields()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var address = new Address { AddressLine1 = "1 Legacy Road", CountryId = 764 };
        var role = new Role { Name = "Director" };
        dbContext.AddRange(address, role);
        await dbContext.SaveChangesAsync();
        var created = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Unspecified);
        var employee = new Employee
        {
            RoleId = role.Id,
            FirstName = "Old",
            LastName = "Name",
            PhoneNumber = "0404",
            Email = "server-owned@maliev.com",
            DateOfBirth = new DateTime(1990, 1, 1),
            HomeAddressId = address.Id,
            CreatedDate = created,
            ModifiedDate = created,
        };
        dbContext.Add(employee);
        await dbContext.SaveChangesAsync();
        var modified = new DateTimeOffset(2030, 7, 18, 3, 4, 5, TimeSpan.Zero);
        var repository = new EmployeeRepository(dbContext, new FakeTimeProvider(modified));

        var updated = await repository.UpdateSelfProfileAsync(
            employee.Id,
            new UpdateEmployeeSelfProfileRequest("  Ada  ", "  Lovelace ", "0690", new DateTime(1815, 12, 10)),
            CancellationToken.None);
        dbContext.ChangeTracker.Clear();
        var persisted = await dbContext.Employees.SingleAsync(value => value.Id == employee.Id);

        Assert.True(updated);
        Assert.Equal("Ada", persisted.FirstName);
        Assert.Equal("Lovelace", persisted.LastName);
        Assert.Equal("0690", persisted.PhoneNumber);
        Assert.Equal(new DateTime(1815, 12, 10), persisted.DateOfBirth);
        Assert.Equal(role.Id, persisted.RoleId);
        Assert.Equal("server-owned@maliev.com", persisted.Email);
        Assert.Equal(address.Id, persisted.HomeAddressId);
        Assert.NotNull(persisted.CreatedDate);
        Assert.NotNull(persisted.ModifiedDate);
        Assert.Equal(DateTimeKind.Unspecified, persisted.CreatedDate.Value.Kind);
        Assert.Equal(DateTimeKind.Unspecified, persisted.ModifiedDate.Value.Kind);
        Assert.Equal(created, persisted.CreatedDate.Value);
        Assert.Equal(DateTime.SpecifyKind(modified.UtcDateTime, DateTimeKind.Unspecified), persisted.ModifiedDate.Value);
    }

    /// <summary>Checks that updating a missing self-profile returns false without inserting an employee.</summary>
    /// <returns>A task that completes after verifying that updating a missing self-profile returns false without inserting an employee.</returns>
    [Fact]
    public async Task UpdateSelfProfileAsync_MissingEmployee_ReturnsFalseWithoutAddingRows()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var repository = new EmployeeRepository(dbContext, TimeProvider.System);

        var updated = await repository.UpdateSelfProfileAsync(
            404,
            new UpdateEmployeeSelfProfileRequest("Ada", "Lovelace", null, null),
            CancellationToken.None);

        Assert.False(updated);
        Assert.Empty(await dbContext.Employees.ToListAsync());
    }

    private EmployeeDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<EmployeeDbContext>().UseNpgsql(postgres.GetConnectionString()).Options;
        return new EmployeeDbContext(options);
    }
}

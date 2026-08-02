using Legacy.Maliev.EmployeeService.Application.Models;
using Legacy.Maliev.EmployeeService.Data;
using Legacy.Maliev.EmployeeService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace Legacy.Maliev.EmployeeService.Tests.Data;

public sealed class EmployeeSelfProfilePostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();

    public Task InitializeAsync() => postgres.StartAsync();

    public Task DisposeAsync() => postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task UpdateSelfProfileAsync_PersistsAllowedFieldsAndPreservesServerOwnedFields()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var address = new Address { AddressLine1 = "1 Legacy Road", CountryId = 764 };
        var role = new Role { Name = "Director" };
        dbContext.AddRange(address, role);
        await dbContext.SaveChangesAsync();
        var created = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
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
        Assert.Equal(created, persisted.CreatedDate);
        Assert.Equal(modified.UtcDateTime, persisted.ModifiedDate);
    }

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

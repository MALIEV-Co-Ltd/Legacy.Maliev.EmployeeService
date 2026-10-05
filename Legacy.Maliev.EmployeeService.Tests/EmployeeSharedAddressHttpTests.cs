using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.EmployeeService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.EmployeeService.Tests;

[CollectionDefinition("Employee shared address", DisableParallelization = true)]
public sealed class EmployeeSharedAddressCollection;

[Collection("Employee shared address")]
public sealed class EmployeeSharedAddressHttpTests(EmployeeRouteAcceptanceFixture fixture) : IClassFixture<EmployeeRouteAcceptanceFixture>
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  Literal address  ")]
    [InlineData("ถนนกรุงเทพ")]
    public async Task SharedAddressUpdate_RefreshesBothEmployeeReadsAndEvictsOnlyLinkedRealRedisKeys(string? value)
    {
        await using var db = fixture.CreateContext();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"Employee\", \"Address\", \"Role\", \"SignatureImageFile\" RESTART IDENTITY CASCADE");
        fixture.Authorities.Clear();
        var shared = new Address { AddressLine1 = "Original road", CountryId = 764, CreatedDate = new DateTime(2020, 1, 1), ModifiedDate = new DateTime(2020, 1, 2) };
        var unrelated = new Address { AddressLine1 = "Unrelated road", CountryId = 840 };
        db.Addresses.AddRange(shared, unrelated);
        await db.SaveChangesAsync();
        db.Employees.AddRange(new Employee { FirstName = "First", LastName = "Fixture", Email = "first@example.test", HomeAddressId = shared.Id },
            new Employee { FirstName = "Second", LastName = "Fixture", Email = "second@example.test", HomeAddressId = shared.Id },
            new Employee { FirstName = "Unrelated", LastName = "Fixture", Email = "unrelated@example.test", HomeAddressId = unrelated.Id });
        await db.SaveChangesAsync();
        var employees = await db.Employees.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync();
        var beforeEmployees = await EmployeeRowsAsync();
        var beforeShared = await db.Addresses.AsNoTracking().SingleAsync(row => row.Id == shared.Id);
        var beforeUnrelated = JsonSerializer.Serialize(await db.Addresses.AsNoTracking().SingleAsync(row => row.Id == unrelated.Id));
        using var scope = fixture.Factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var old = Encoding.UTF8.GetBytes("""{"id":999,"firstName":"Old cache","lastName":"Fixture","fullName":"Old cache Fixture","email":"old@example.test"}""");
        foreach (var employee in employees)
        {
            await cache.SetAsync($"employee:{employee.Id}", old,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) });
            Assert.Equal(old, await cache.GetAsync($"employee:{employee.Id}"));
        }
        using var writer = fixture.Client("legacy-employee.addresses.update", $"/employees/addresses/{shared.Id}");
        using var response = await writer.PutAsJsonAsync($"/employees/addresses/{shared.Id}", new
        {
            Id = 999999,
            AddressLine1 = value,
            AddressLine2 = value,
            Building = value,
            City = value,
            State = value,
            PostalCode = value,
            CountryId = 392,
            CreatedDate = new DateTime(1900, 1, 1),
            ModifiedDate = new DateTime(1900, 1, 1)
        });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        foreach (var employee in employees.Where(row => row.HomeAddressId == shared.Id))
            Assert.Null(await cache.GetAsync($"employee:{employee.Id}"));
        var unrelatedEmployee = Assert.Single(employees, row => row.HomeAddressId == unrelated.Id);
        Assert.Equal(old, await cache.GetAsync($"employee:{unrelatedEmployee.Id}"));
        Assert.Equal(beforeEmployees, await EmployeeRowsAsync());
        Assert.Equal(beforeUnrelated, JsonSerializer.Serialize(await db.Addresses.AsNoTracking().SingleAsync(row => row.Id == unrelated.Id)));
        var changed = await db.Addresses.AsNoTracking().SingleAsync(row => row.Id == shared.Id);
        Assert.Equal(shared.Id, changed.Id);
        Assert.Equal(beforeShared.CreatedDate, changed.CreatedDate);
        Assert.True(changed.ModifiedDate > beforeShared.ModifiedDate);
        Assert.Equal(392, changed.CountryId);
        Assert.Equal(Enumerable.Repeat(value, 6), new[] { changed.AddressLine1, changed.AddressLine2, changed.Building, changed.City, changed.State, changed.PostalCode });
        foreach (var employee in employees.Where(row => row.HomeAddressId == shared.Id))
        {
            using var reader = fixture.Client("legacy-employee.employees.read", $"/employees/{employee.Id}");
            using var detail = await reader.GetAsync($"/employees/{employee.Id}");
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            using var json = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
            Assert.Equal(employee.Id, json.RootElement.GetProperty("Id").GetInt32());
            Assert.Equal(employee.FullName, json.RootElement.GetProperty("FullName").GetString());
            Assert.Equal(shared.Id, json.RootElement.GetProperty("HomeAddressId").GetInt32());
            AssertAddress(json.RootElement.GetProperty("HomeAddress"), value);
        }
        using var addressReader = fixture.Client("legacy-employee.addresses.read", $"/employees/addresses/{shared.Id}");
        using var addressDetail = await addressReader.GetAsync($"/employees/addresses/{shared.Id}");
        Assert.Equal(HttpStatusCode.OK, addressDetail.StatusCode);
        using var addressJson = JsonDocument.Parse(await addressDetail.Content.ReadAsStringAsync());
        AssertAddress(addressJson.RootElement, value);
        Assert.Equal(beforeEmployees, await EmployeeRowsAsync());
        Assert.Equal(old, await cache.GetAsync($"employee:{unrelatedEmployee.Id}"));
        Assert.False(await db.Roles.AnyAsync());
        Assert.False(await db.SignatureImageFiles.AnyAsync());
    }

    private async Task<string> EmployeeRowsAsync()
    {
        await using var db = fixture.CreateContext();
        return JsonSerializer.Serialize(await db.Employees.AsNoTracking().OrderBy(row => row.Id)
            .Select(row => new { row.Id, row.FirstName, row.LastName, row.FullName, row.Email, row.PhoneNumber, row.DateOfBirth, row.RoleId, row.HomeAddressId, row.CreatedDate, row.ModifiedDate }).ToArrayAsync());
    }

    private static void AssertAddress(JsonElement address, string? value)
    {
        Assert.Equal(392, address.GetProperty("CountryId").GetInt32());
        foreach (var field in new[] { "AddressLine1", "AddressLine2", "Building", "City", "State", "PostalCode" })
        {
            if (value is null) Assert.False(address.TryGetProperty(field, out _));
            else Assert.Equal(value, address.GetProperty(field).GetString());
        }
        Assert.False(address.TryGetProperty("addressLine1", out _));
    }
}

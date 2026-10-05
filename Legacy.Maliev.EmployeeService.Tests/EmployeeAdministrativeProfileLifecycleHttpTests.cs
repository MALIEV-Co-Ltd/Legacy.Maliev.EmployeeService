using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.EmployeeService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.EmployeeService.Tests;

[CollectionDefinition("Employee administrative profile", DisableParallelization = true)]
public sealed class EmployeeAdministrativeProfileCollection;

[Collection("Employee administrative profile")]
public sealed class EmployeeAdministrativeProfileLifecycleHttpTests(EmployeeRouteAcceptanceFixture fixture)
    : IClassFixture<EmployeeRouteAcceptanceFixture>
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" 0900000000 ", true)]
    [InlineData("0900000000", true)]
    public async Task AdministrativeProfile_CreatePreservesLiteralOptionalFieldsAndUpdateCanClearThem(string? phone, bool related)
    {
        await using var db = fixture.CreateContext();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"Employee\", \"Address\", \"Role\", \"SignatureImageFile\" RESTART IDENTITY CASCADE");
        var address = new Address { AddressLine1 = "Original road", CountryId = 764 };
        var role = new Role { Name = "Original role" };
        db.Addresses.Add(address);
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        var beforeAddress = JsonSerializer.Serialize(await db.Addresses.AsNoTracking().SingleAsync());
        var beforeRole = JsonSerializer.Serialize(await db.Roles.AsNoTracking().SingleAsync());
        fixture.Authorities.Clear();
        using var creator = fixture.Client("legacy-employee.employees.create");
        using var created = await creator.PostAsJsonAsync("/employees/", new
        {
            Id = 999,
            FirstName = "วิศวกร",
            LastName = "Fixture",
            Email = "administrative-profile@example.test",
            PhoneNumber = phone,
            DateOfBirth = related ? new DateTime(1980, 1, 2) : (DateTime?)null,
            RoleId = related ? role.Id : (int?)null,
            HomeAddressId = related ? address.Id : (int?)null,
            CreatedDate = new DateTime(1900, 1, 1),
            ModifiedDate = new DateTime(1900, 1, 1)
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = createJson.RootElement.GetProperty("Id").GetInt32();
        Assert.NotEqual(999, id);
        Assert.EndsWith($"/Employees/{id}", created.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
        var original = await db.Employees.AsNoTracking().SingleAsync();
        Assert.Equal("วิศวกร Fixture", original.FullName);
        Assert.Equal(phone, original.PhoneNumber);
        Assert.Equal(related ? new DateTime(1980, 1, 2) : (DateTime?)null, original.DateOfBirth);
        Assert.Equal(related ? role.Id : (int?)null, original.RoleId);
        Assert.Equal(related ? address.Id : (int?)null, original.HomeAddressId);
        Assert.True(original.CreatedDate > new DateTime(2020, 1, 1));
        using var reader = fixture.Client("legacy-employee.employees.read", $"/employees/{id}");
        using var read = await reader.GetAsync(created.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var readJson = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.Equal("วิศวกร Fixture", readJson.RootElement.GetProperty("FullName").GetString());
        Assert.False(readJson.RootElement.TryGetProperty("firstName", out _));
        Assert.False(readJson.RootElement.TryGetProperty("Role", out _));
        if (phone is null) Assert.False(readJson.RootElement.TryGetProperty("PhoneNumber", out _));
        else Assert.Equal(phone, readJson.RootElement.GetProperty("PhoneNumber").GetString());
        if (related)
        {
            Assert.Equal(role.Id, readJson.RootElement.GetProperty("RoleId").GetInt32());
            Assert.Equal(address.Id, readJson.RootElement.GetProperty("HomeAddressId").GetInt32());
            Assert.Equal("Original road", readJson.RootElement.GetProperty("HomeAddress").GetProperty("AddressLine1").GetString());
            Assert.Equal(new DateTime(1980, 1, 2), readJson.RootElement.GetProperty("DateOfBirth").GetDateTime());
        }
        else
        {
            foreach (var field in new[] { "RoleId", "HomeAddressId", "HomeAddress", "DateOfBirth" })
                Assert.False(readJson.RootElement.TryGetProperty(field, out _));
        }
        using var scope = fixture.Factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        await cache.SetAsync($"employee:{id}", Encoding.UTF8.GetBytes("""{"id":1,"firstName":"Old writer","lastName":"Fixture","fullName":"Old writer Fixture","email":"administrative-profile@example.test"}"""),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) });
        Assert.NotNull(await cache.GetAsync($"employee:{id}"));
        using var updater = fixture.Client("legacy-employee.employees.update", $"/employees/{id}");
        using var updated = await updater.PutAsJsonAsync($"/employees/{id}/", new
        {
            FirstName = "Updated",
            LastName = "Profile",
            Email = "updated-administrative@example.test",
            PhoneNumber = (string?)null,
            DateOfBirth = (DateTime?)null,
            RoleId = (int?)null,
            HomeAddressId = (int?)null,
            CreatedDate = new DateTime(1900, 1, 1)
        });
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
        Assert.Null(await cache.GetAsync($"employee:{id}"));
        var cleared = await db.Employees.AsNoTracking().SingleAsync();
        Assert.Equal(original.CreatedDate, cleared.CreatedDate);
        Assert.True(cleared.ModifiedDate > original.ModifiedDate);
        Assert.Equal("Updated Profile", cleared.FullName);
        Assert.Equal("updated-administrative@example.test", cleared.Email);
        Assert.Null(cleared.PhoneNumber);
        Assert.Null(cleared.DateOfBirth);
        Assert.Null(cleared.RoleId);
        Assert.Null(cleared.HomeAddressId);
        using var clearedRead = await reader.GetAsync(created.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, clearedRead.StatusCode);
        using var clearedJson = JsonDocument.Parse(await clearedRead.Content.ReadAsStringAsync());
        Assert.Equal("Updated Profile", clearedJson.RootElement.GetProperty("FullName").GetString());
        foreach (var field in new[] { "PhoneNumber", "DateOfBirth", "RoleId", "HomeAddressId", "HomeAddress", "Role" })
            Assert.False(clearedJson.RootElement.TryGetProperty(field, out _));
        Assert.Equal(beforeAddress, JsonSerializer.Serialize(await db.Addresses.AsNoTracking().SingleAsync()));
        Assert.Equal(beforeRole, JsonSerializer.Serialize(await db.Roles.AsNoTracking().SingleAsync()));
    }
}

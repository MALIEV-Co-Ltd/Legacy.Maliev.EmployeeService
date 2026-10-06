using System.Net;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.EmployeeService.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.EmployeeService.Tests;

[Collection("Employee shared address")]
public sealed class EmployeeProfileDeletionIsolationHttpTests(EmployeeRouteAcceptanceFixture fixture)
    : IClassFixture<EmployeeRouteAcceptanceFixture>
{
    [Theory]
    [InlineData("allow")]
    [InlineData("deny")]
    public async Task DeleteProfile_RequiresLiveIamAndPreservesSharedGraphAndSurvivorRedisKeys(string decision)
    {
        Employee[] employees;
        Address[] addresses;
        await using (var db = fixture.CreateContext())
        {
            var oldIds = await db.Employees.AsNoTracking().Select(employee => employee.Id).ToArrayAsync();
            var oldCache = fixture.Factory.Services.GetRequiredService<IDistributedCache>();
            foreach (var id in oldIds) await oldCache.RemoveAsync($"employee:{id}");
            await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"Employee\", \"Address\", \"Role\", \"SignatureImageFile\" CASCADE");
            fixture.Authorities.Clear();
            addresses =
            [
                new Address { AddressLine1 = "Shared road", City = "Bangkok", CountryId = 764 },
                new Address { AddressLine1 = "Unrelated road", City = "Chiang Mai", CountryId = 764 }
            ];
            var roles = new[]
            {
                new Role { Name = "Shared role", Description = "Shared business role" },
                new Role { Name = "Unrelated role", Description = "Unrelated business role" }
            };
            db.Addresses.AddRange(addresses);
            db.Roles.AddRange(roles);
            await db.SaveChangesAsync();
            employees =
            [
                Employee("Selected", "selected@example.test", addresses[0].Id, roles[0].Id),
                Employee("Shared survivor", "shared-survivor@example.test", addresses[0].Id, roles[0].Id),
                Employee("Unrelated survivor", "unrelated-survivor@example.test", addresses[1].Id, roles[1].Id)
            ];
            db.Employees.AddRange(employees);
            await db.SaveChangesAsync();
            foreach (var employee in employees)
            {
                db.SignatureImageFiles.Add(new SignatureImageFile
                {
                    EmployeeId = employee.Id,
                    Bucket = "fixture-private",
                    ObjectName = $"signatures/isolation-{employee.Id}.png"
                });
            }
            await db.SaveChangesAsync();
        }
        var target = employees[0];
        await using (var readback = fixture.CreateContext())
        {
            var rows = await readback.Employees.AsNoTracking().OrderBy(employee => employee.Id).ToArrayAsync();
            Assert.Equal(employees.Select(employee => employee.Id).OrderBy(id => id).ToArray(), rows.Select(employee => employee.Id).ToArray());
            foreach (var expected in employees)
            {
                var stored = Assert.Single(rows, employee => employee.Id == expected.Id);
                Assert.Equal(expected.FirstName, stored.FirstName);
                Assert.Equal(expected.RoleId, stored.RoleId);
                Assert.Equal(expected.HomeAddressId, stored.HomeAddressId);
            }
            Assert.Equal(2, await readback.Addresses.CountAsync());
            Assert.Equal(2, await readback.Roles.CountAsync());
            Assert.Equal(3, await readback.SignatureImageFiles.CountAsync());
        }
        var before = await SnapshotAsync();
        var expectedAfter = decision == "allow" ? await SnapshotAsync(target.Id) : before;
        await using var first = fixture.NewFactory(withIam: true);
        await using var second = fixture.NewFactory(withIam: true);
        var firstCache = first.Services.GetRequiredService<IDistributedCache>();
        var secondCache = second.Services.GetRequiredService<IDistributedCache>();
        var cacheBytes = employees.ToDictionary(employee => employee.Id,
            employee => CacheBytes(employee, addresses.Single(address => address.Id == employee.HomeAddressId)));
        foreach (var employee in employees)
        {
            await firstCache.SetAsync($"employee:{employee.Id}", cacheBytes[employee.Id],
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) });
        }
        await AssertCacheBytesAsync(firstCache, cacheBytes);
        await AssertCacheBytesAsync(secondCache, cacheBytes);
        using var writer = fixture.Client("legacy-employee.employees.delete", $"/employees/{target.Id}", decision: decision, factory: first);
        var authority = Assert.Single(fixture.Authorities.Values, item => item.Permission == "legacy-employee.employees.delete");
        using (var response = await writer.DeleteAsync($"/employees/{target.Id}"))
        {
            Assert.Equal(decision == "allow" ? HttpStatusCode.NoContent : HttpStatusCode.Forbidden, response.StatusCode);
        }
        Assert.Equal(decision, authority.Decision);
        Assert.Equal($"/employees/{target.Id}", authority.Resource);
        Assert.Equal(1, authority.LiveCalls);
        Assert.True(authority.Calls >= authority.LiveCalls);
        fixture.IamTransportEvidence.AssertHealthy(); // Includes bypassCache and live-check header validation.
        Assert.Equal(expectedAfter, await SnapshotAsync());
        var survivingBytes = cacheBytes.Where(pair => decision != "allow" || pair.Key != target.Id)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        await AssertCacheBytesAsync(firstCache, survivingBytes);
        await AssertCacheBytesAsync(secondCache, survivingBytes);
        if (decision == "allow")
        {
            Assert.Null(await firstCache.GetAsync($"employee:{target.Id}"));
            Assert.Null(await secondCache.GetAsync($"employee:{target.Id}"));
            // Delayed old-writer DTO publication still reaches actual Redis after acknowledged deletion.
            await secondCache.SetAsync($"employee:{target.Id}", cacheBytes[target.Id],
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) });
            Assert.Equal(cacheBytes[target.Id], await firstCache.GetAsync($"employee:{target.Id}"));
            foreach (var host in new[] { first, second })
            {
                using var reader = fixture.Client("legacy-employee.employees.read", $"/employees/{target.Id}", factory: host);
                using var missing = await reader.GetAsync($"/employees/{target.Id}");
                Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
                var body = await missing.Content.ReadAsStringAsync();
                Assert.DoesNotContain("selected@example.test", body, StringComparison.Ordinal);
                Assert.DoesNotContain("\"FirstName\"", body, StringComparison.Ordinal);
                Assert.DoesNotContain("PasswordHash", body, StringComparison.Ordinal);
            }
            using var repeated = await writer.DeleteAsync($"/employees/{target.Id}");
            Assert.Equal(HttpStatusCode.NotFound, repeated.StatusCode);
            Assert.Equal(2, authority.LiveCalls);
        }
        var expectedEmployees = employees.Where(employee => decision != "allow" || employee.Id != target.Id).ToArray();
        foreach (var host in new[] { first, second })
        {
            foreach (var employee in expectedEmployees)
            {
                var address = addresses.Single(item => item.Id == employee.HomeAddressId);
                await AssertDetailAsync(host, employee, address);
            }
            using var listClient = fixture.Client("legacy-employee.employees.list", factory: host);
            using var list = await listClient.GetAsync("/employees");
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
            Assert.Equal(expectedEmployees.Length, listJson.RootElement.GetProperty("TotalRecords").GetInt32());
            Assert.Equal(expectedEmployees.Select(employee => employee.Id).OrderBy(id => id).ToArray(),
                listJson.RootElement.GetProperty("Items").EnumerateArray().Select(item => item.GetProperty("Id").GetInt32()).ToArray());
        }
        Assert.Equal(expectedAfter, await SnapshotAsync());
        await AssertCacheBytesAsync(firstCache, survivingBytes);
        await AssertCacheBytesAsync(secondCache, survivingBytes);
        fixture.IamTransportEvidence.AssertHealthy();
    }

    private async Task AssertDetailAsync(WebApplicationFactory<Program> host, Employee employee, Address address)
    {
        using var client = fixture.Client("legacy-employee.employees.read", $"/employees/{employee.Id}", factory: host);
        using var response = await client.GetAsync($"/employees/{employee.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var json = document.RootElement;
        Assert.Equal(employee.Id, json.GetProperty("Id").GetInt32());
        Assert.Equal(employee.FirstName, json.GetProperty("FirstName").GetString());
        Assert.Equal(employee.Email, json.GetProperty("Email").GetString());
        Assert.Equal(employee.RoleId, json.GetProperty("RoleId").GetInt32());
        Assert.Equal(address.Id, json.GetProperty("HomeAddressId").GetInt32());
        Assert.Equal(address.Id, json.GetProperty("HomeAddress").GetProperty("Id").GetInt32());
        Assert.Equal(address.AddressLine1, json.GetProperty("HomeAddress").GetProperty("AddressLine1").GetString());
        Assert.False(json.TryGetProperty("id", out _));
        Assert.False(json.TryGetProperty("Role", out _)); // Source employee detail embeds only HomeAddress.
        foreach (var field in new[] { "Password", "PasswordHash", "SecurityStamp", "EmployeeIdentity", "SignatureImageFile" })
            Assert.False(json.TryGetProperty(field, out _));
        Assert.False(json.GetProperty("HomeAddress").TryGetProperty("Employees", out _));
        using var roleClient = fixture.Client("legacy-employee.roles.read", factory: host);
        using var roleResponse = await roleClient.GetAsync($"/employees/roles/{employee.RoleId}");
        Assert.Equal(HttpStatusCode.OK, roleResponse.StatusCode);
        using var roleJson = JsonDocument.Parse(await roleResponse.Content.ReadAsStringAsync());
        Assert.Equal(employee.RoleId, roleJson.RootElement.GetProperty("Id").GetInt32());
        Assert.Equal(employee.FirstName == "Unrelated survivor" ? "Unrelated role" : "Shared role", roleJson.RootElement.GetProperty("Name").GetString());
        Assert.False(roleJson.RootElement.TryGetProperty("Employees", out _));
    }

    private static Employee Employee(string name, string email, int addressId, int roleId) => new()
    {
        FirstName = name,
        LastName = "Fixture",
        Email = email,
        PhoneNumber = "0800",
        HomeAddressId = addressId,
        RoleId = roleId
    };

    private static byte[] CacheBytes(Employee employee, Address address) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
    {
        id = employee.Id,
        firstName = employee.FirstName,
        lastName = "Fixture",
        fullName = employee.FirstName + " Fixture",
        email = employee.Email,
        phoneNumber = "0800",
        roleId = employee.RoleId,
        homeAddressId = address.Id,
        homeAddress = new { id = address.Id, addressLine1 = address.AddressLine1, city = address.City, countryId = 764 }
    }));

    private static async Task AssertCacheBytesAsync(IDistributedCache cache, IReadOnlyDictionary<int, byte[]> expected)
    {
        foreach (var pair in expected) Assert.Equal(pair.Value, await cache.GetAsync($"employee:{pair.Key}"));
    }

    private async Task<string> SnapshotAsync(int? excludedEmployeeId = null)
    {
        await using var db = fixture.CreateContext();
        return JsonSerializer.Serialize(new
        {
            Employees = await db.Employees.AsNoTracking().Where(employee => excludedEmployeeId == null || employee.Id != excludedEmployeeId)
                .OrderBy(employee => employee.Id).Select(employee => new
                {
                    employee.Id,
                    employee.RoleId,
                    employee.FirstName,
                    employee.LastName,
                    employee.FullName,
                    employee.PhoneNumber,
                    employee.Email,
                    employee.DateOfBirth,
                    employee.HomeAddressId,
                    employee.CreatedDate,
                    employee.ModifiedDate
                }).ToArrayAsync(),
            Addresses = await db.Addresses.AsNoTracking().OrderBy(address => address.Id).Select(address => new
            {
                address.Id,
                address.Building,
                address.AddressLine1,
                address.AddressLine2,
                address.City,
                address.State,
                address.PostalCode,
                address.CountryId,
                address.CreatedDate,
                address.ModifiedDate
            }).ToArrayAsync(),
            Roles = await db.Roles.AsNoTracking().OrderBy(role => role.Id).Select(role => new
            {
                role.Id,
                role.Name,
                role.Description,
                role.CreatedDate,
                role.ModifiedDate
            }).ToArrayAsync(),
            Signatures = await db.SignatureImageFiles.AsNoTracking().OrderBy(signature => signature.Id).Select(signature => new
            {
                signature.Id,
                signature.EmployeeId,
                signature.Bucket,
                signature.ObjectName,
                signature.CreatedDate,
                signature.ModifiedDate
            }).ToArrayAsync()
        });
    }
}

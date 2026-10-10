using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.EmployeeService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.EmployeeService.Tests;

[CollectionDefinition("Employee administrative persistence", DisableParallelization = true)]
public sealed class EmployeeAdministrativePersistenceCollection;

[Collection("Employee administrative persistence")]
public sealed class EmployeeAdministrativePersistenceHttpTests(EmployeeRouteAcceptanceFixture fixture)
    : IClassFixture<EmployeeRouteAcceptanceFixture>
{
    [Theory]
    [InlineData("FirstName", false)]
    [InlineData("LastName", false)]
    [InlineData("Email", false)]
    [InlineData("PhoneNumber", false)]
    [InlineData("FirstName", true)]
    [InlineData("LastName", true)]
    [InlineData("Email", true)]
    [InlineData("PhoneNumber", true)]
    public async Task AdministrativeLengthBoundary_Persists256AndRejects257AtomicallyWithoutEvictingCache(string field, bool update)
    {
        await SeedAsync();
        using var scope = fixture.Factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var ownerCache = await PrimeAsync(cache, 17);
        var otherCache = await PrimeAsync(cache, 18);
        var unrelatedBefore = await SnapshotAsync(update ? 17 : null);
        using var writer = fixture.Client(update ? "legacy-employee.employees.update" : "legacy-employee.employees.create",
            update ? "/employees/17" : "global");
        var valid = Payload(field, 256);
        using var accepted = await SendAsync(writer, update, valid);
        Assert.Equal(update ? HttpStatusCode.NoContent : HttpStatusCode.Created, accepted.StatusCode);
        var id = 17;
        if (update)
        {
            Assert.Null(await cache.GetAsync("employee:17"));
            Assert.Equal(string.Empty, await accepted.Content.ReadAsStringAsync());
        }
        else
        {
            using var created = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync());
            id = created.RootElement.GetProperty("Id").GetInt32();
            Assert.NotEqual(999999, id);
            Assert.EndsWith($"/Employees/{id}", accepted.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(ownerCache, await cache.GetAsync("employee:17"));
        }
        Assert.Equal(otherCache, await cache.GetAsync("employee:18"));
        Assert.Equal(unrelatedBefore, await SnapshotAsync(id));

        await using (var db = fixture.CreateContext())
        {
            var saved = await db.Employees.AsNoTracking().SingleAsync(row => row.Id == id);
            Assert.Equal(valid["FirstName"], saved.FirstName);
            Assert.Equal(valid["LastName"], saved.LastName);
            Assert.Equal(valid["Email"], saved.Email);
            Assert.Equal(valid["PhoneNumber"], saved.PhoneNumber);
            Assert.Equal($"{saved.FirstName} {saved.LastName}", saved.FullName);
            Assert.Equal(1, saved.RoleId);
            Assert.Equal(1, saved.HomeAddressId);
            Assert.Equal(new DateTime(1980, 1, 2), saved.DateOfBirth);
            Assert.True(saved.ModifiedDate > new DateTime(2020, 1, 2));
            if (update) Assert.Equal(new DateTime(2020, 1, 1), saved.CreatedDate);
            else Assert.True(saved.CreatedDate > new DateTime(2020, 1, 1));
        }
        using var reader = fixture.Client("legacy-employee.employees.read", $"/employees/{id}");
        using var read = await reader.GetAsync($"/employees/{id}/");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var json = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.Equal((string)valid[field]!, json.RootElement.GetProperty(field).GetString());
        Assert.Equal("Owner road", json.RootElement.GetProperty("HomeAddress").GetProperty("AddressLine1").GetString());
        Assert.False(json.RootElement.TryGetProperty("firstName", out _));
        Assert.False(json.RootElement.TryGetProperty("Role", out _));

        // Reprime independently after a successful update. Failed persistence must leave these bytes intact.
        var beforeOwnerCache = await PrimeAsync(cache, 17);
        var before = await SnapshotAsync();
        var invalid = Payload(field, 257);
        invalid["RoleId"] = 2;
        invalid["HomeAddressId"] = 2;
        invalid["DateOfBirth"] = new DateTime(1990, 2, 3);
        using var rejected = await SendAsync(writer, update, invalid);
        var nameBoundary = field is "FirstName" or "LastName";
        Assert.Equal(nameBoundary ? HttpStatusCode.BadRequest : HttpStatusCode.InternalServerError, rejected.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
        Assert.Equal(beforeOwnerCache, await cache.GetAsync("employee:17"));
        Assert.Equal(otherCache, await cache.GetAsync("employee:18"));
        if (!nameBoundary)
        {
            using var error = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
            Assert.Equal(500, error.RootElement.GetProperty("statusCode").GetInt32());
            Assert.Equal(JsonValueKind.Null, error.RootElement.GetProperty("details").ValueKind);
            Assert.False(string.IsNullOrWhiteSpace(error.RootElement.GetProperty("traceId").GetString()));
        }
        var body = await rejected.Content.ReadAsStringAsync();
        foreach (var marker in new[] { (string)invalid[field]!, "Npgsql", "character varying", "owner@example.invalid" })
            Assert.DoesNotContain(marker, body, StringComparison.OrdinalIgnoreCase);
        fixture.IamTransportEvidence.AssertHealthy();
    }

    private static Dictionary<string, object?> Payload(string field, int length)
    {
        var payload = new Dictionary<string, object?>
        {
            ["Id"] = 999999,
            ["FirstName"] = "Updated",
            ["LastName"] = "Synthetic",
            ["Email"] = "updated@example.invalid",
            ["PhoneNumber"] = "0900000000",
            ["RoleId"] = 1,
            ["HomeAddressId"] = 1,
            ["DateOfBirth"] = new DateTime(1980, 1, 2),
            ["CreatedDate"] = new DateTime(1900, 1, 1),
            ["ModifiedDate"] = new DateTime(1900, 1, 1)
        };
        payload[field] = field == "Email" ? new string('e', length - "@example.invalid".Length) + "@example.invalid"
            : new string(field == "PhoneNumber" ? '0' : 'ก', length);
        return payload;
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, bool update, Dictionary<string, object?> payload) => update
        ? client.PutAsJsonAsync("/employees/17/", payload)
        : client.PostAsJsonAsync("/employees/", payload);

    private static async Task<byte[]> PrimeAsync(IDistributedCache cache, int id)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            id,
            firstName = "Cached",
            lastName = "Synthetic",
            fullName = "Cached Synthetic",
            email = $"cached-{id}@example.invalid"
        }));
        await cache.SetAsync($"employee:{id}", bytes,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) });
        Assert.Equal(bytes, await cache.GetAsync($"employee:{id}"));
        return bytes;
    }

    private async Task SeedAsync()
    {
        await using var db = fixture.CreateContext();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"Employee\", \"Address\", \"Role\", \"SignatureImageFile\" RESTART IDENTITY CASCADE");
        db.Addresses.AddRange(new Address { Id = 1, AddressLine1 = "Owner road", CountryId = 764 },
            new Address { Id = 2, AddressLine1 = "Other road", CountryId = 764 });
        db.Roles.AddRange(new Role { Id = 1, Name = "Owner role" }, new Role { Id = 2, Name = "Other role" });
        db.Employees.AddRange(new Employee
        {
            Id = 17,
            FirstName = "Owner",
            LastName = "Synthetic",
            Email = "owner@example.invalid",
            RoleId = 1,
            HomeAddressId = 1,
            CreatedDate = new DateTime(2020, 1, 1),
            ModifiedDate = new DateTime(2020, 1, 2)
        }, new Employee
        {
            Id = 18,
            FirstName = "Other",
            LastName = "Synthetic",
            Email = "other@example.invalid",
            RoleId = 2,
            HomeAddressId = 2,
            CreatedDate = new DateTime(2020, 1, 1),
            ModifiedDate = new DateTime(2020, 1, 2)
        });
        db.SignatureImageFiles.Add(new SignatureImageFile { Id = 101, EmployeeId = 17, Bucket = "private", ObjectName = "synthetic.png" });
        await db.SaveChangesAsync();
        fixture.Authorities.Clear();
    }

    private async Task<string> SnapshotAsync(int? excludedEmployeeId = null)
    {
        await using var db = fixture.CreateContext();
        return JsonSerializer.Serialize(new
        {
            Employees = await db.Employees.AsNoTracking().Where(row => excludedEmployeeId == null || row.Id != excludedEmployeeId).OrderBy(row => row.Id).ToArrayAsync(),
            Addresses = await db.Addresses.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Roles = await db.Roles.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Signatures = await db.SignatureImageFiles.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync()
        });
    }
}

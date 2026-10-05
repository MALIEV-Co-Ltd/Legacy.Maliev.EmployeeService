using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.EmployeeService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.EmployeeService.Tests;

[CollectionDefinition("Employee address persistence", DisableParallelization = true)]
public sealed class EmployeeAddressPersistenceCollection;

[Collection("Employee address persistence")]
public sealed class EmployeeAddressPersistenceHttpTests(EmployeeRouteAcceptanceFixture fixture)
    : IClassFixture<EmployeeRouteAcceptanceFixture>
{
    [Theory]
    [InlineData("AddressLine1", false)]
    [InlineData("AddressLine2", false)]
    [InlineData("Building", false)]
    [InlineData("City", false)]
    [InlineData("State", false)]
    [InlineData("PostalCode", false)]
    [InlineData("AddressLine1", true)]
    [InlineData("AddressLine2", true)]
    [InlineData("Building", true)]
    [InlineData("City", true)]
    [InlineData("State", true)]
    [InlineData("PostalCode", true)]
    public async Task AddressLengthBoundary_Persists256AndRejects257WithoutChangingGraphOrLinkedCache(string field, bool update)
    {
        await SeedAsync();
        using var scope = fixture.Factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var old = await PrimeAsync(cache);
        var unrelatedBefore = await SnapshotAsync(update ? 101 : null);
        using var writer = fixture.Client(update ? "legacy-employee.addresses.update" : "legacy-employee.addresses.create",
            update ? "/employees/addresses/101" : "global");
        var valid = Payload(field, 256);
        using var accepted = await SendAsync(writer, update, valid);
        Assert.Equal(update ? HttpStatusCode.NoContent : HttpStatusCode.Created, accepted.StatusCode);
        var id = 101;
        if (update)
        {
            Assert.Null(await cache.GetAsync("employee:17"));
            Assert.Null(await cache.GetAsync("employee:18"));
            Assert.Equal(string.Empty, await accepted.Content.ReadAsStringAsync());
        }
        else
        {
            using var created = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync());
            id = created.RootElement.GetProperty("Id").GetInt32();
            Assert.NotEqual(999999, id);
            Assert.EndsWith($"/Addresses/{id}", accepted.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
            foreach (var employeeId in new[] { 17, 18 }) Assert.Equal(old[employeeId], await cache.GetAsync($"employee:{employeeId}"));
        }
        Assert.Equal(old[19], await cache.GetAsync("employee:19"));
        Assert.Equal(unrelatedBefore, await SnapshotAsync(id));
        await using (var db = fixture.CreateContext())
        {
            var saved = await db.Addresses.AsNoTracking().SingleAsync(row => row.Id == id);
            Assert.Equal(valid["AddressLine1"], saved.AddressLine1);
            Assert.Equal(valid["AddressLine2"], saved.AddressLine2);
            Assert.Equal(valid["Building"], saved.Building);
            Assert.Equal(valid["City"], saved.City);
            Assert.Equal(valid["State"], saved.State);
            Assert.Equal(valid["PostalCode"], saved.PostalCode);
            Assert.Equal(392, saved.CountryId);
            Assert.True(saved.ModifiedDate > new DateTime(2020, 1, 2));
            if (update) Assert.Equal(new DateTime(2020, 1, 1), saved.CreatedDate);
            else Assert.True(saved.CreatedDate > new DateTime(2020, 1, 1));
            if (!update) Assert.False(await db.Employees.AnyAsync(row => row.HomeAddressId == id));
        }
        using var reader = fixture.Client("legacy-employee.addresses.read", $"/employees/addresses/{id}");
        using var read = await reader.GetAsync($"/employees/addresses/{id}/");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var json = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.Equal((string)valid[field]!, json.RootElement.GetProperty(field).GetString());
        Assert.Equal(392, json.RootElement.GetProperty("CountryId").GetInt32());
        Assert.False(json.RootElement.TryGetProperty("addressLine1", out _));
        if (update)
        {
            foreach (var employeeId in new[] { 17, 18 })
            {
                using var employeeReader = fixture.Client("legacy-employee.employees.read", $"/employees/{employeeId}");
                using var employeeRead = await employeeReader.GetAsync($"/employees/{employeeId}/");
                Assert.Equal(HttpStatusCode.OK, employeeRead.StatusCode);
                using var employeeJson = JsonDocument.Parse(await employeeRead.Content.ReadAsStringAsync());
                Assert.Equal(101, employeeJson.RootElement.GetProperty("HomeAddressId").GetInt32());
                Assert.Equal((string)valid[field]!, employeeJson.RootElement.GetProperty("HomeAddress").GetProperty(field).GetString());
            }
        }

        var beforeCache = await PrimeAsync(cache);
        var before = await SnapshotAsync();
        var invalid = Payload(field, 257);
        invalid["CountryId"] = 840;
        using var rejected = await SendAsync(writer, update, invalid);
        Assert.Equal(HttpStatusCode.InternalServerError, rejected.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
        foreach (var employeeId in new[] { 17, 18, 19 })
            Assert.Equal(beforeCache[employeeId], await cache.GetAsync($"employee:{employeeId}"));
        using var error = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        Assert.Equal(500, error.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal(JsonValueKind.Null, error.RootElement.GetProperty("details").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(error.RootElement.GetProperty("traceId").GetString()));
        var body = await rejected.Content.ReadAsStringAsync();
        foreach (var marker in new[] { (string)invalid[field]!, "Npgsql", "character varying", "Owner road" })
            Assert.DoesNotContain(marker, body, StringComparison.OrdinalIgnoreCase);
        fixture.IamTransportEvidence.AssertHealthy();
    }

    private static Dictionary<string, object?> Payload(string field, int length)
    {
        var payload = new Dictionary<string, object?>
        {
            ["Id"] = 999999,
            ["AddressLine1"] = "Replacement road",
            ["AddressLine2"] = "Replacement line",
            ["Building"] = "Replacement building",
            ["City"] = "Replacement city",
            ["State"] = "Replacement state",
            ["PostalCode"] = "10100",
            ["CountryId"] = 392,
            ["CreatedDate"] = new DateTime(1900, 1, 1),
            ["ModifiedDate"] = new DateTime(1900, 1, 1)
        };
        payload[field] = new string('ก', length);
        return payload;
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, bool update, Dictionary<string, object?> payload) => update
        ? client.PutAsJsonAsync("/employees/addresses/101", payload)
        : client.PostAsJsonAsync("/employees/addresses/", payload);

    private static async Task<Dictionary<int, byte[]>> PrimeAsync(IDistributedCache cache)
    {
        var entries = new Dictionary<int, byte[]>();
        foreach (var id in new[] { 17, 18, 19 })
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
            entries.Add(id, bytes);
        }
        return entries;
    }

    private async Task SeedAsync()
    {
        await using var db = fixture.CreateContext();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"Employee\", \"Address\", \"Role\", \"SignatureImageFile\" RESTART IDENTITY CASCADE");
        db.Addresses.AddRange(new Address
        {
            Id = 101,
            AddressLine1 = "Owner road",
            CountryId = 764,
            CreatedDate = new DateTime(2020, 1, 1),
            ModifiedDate = new DateTime(2020, 1, 2)
        }, new Address { Id = 201, AddressLine1 = "Other road", CountryId = 840 });
        db.Roles.Add(new Role { Id = 1, Name = "Synthetic role" });
        foreach (var id in new[] { 17, 18, 19 }) db.Employees.Add(new Employee
        {
            Id = id,
            FirstName = "Synthetic",
            LastName = $"Employee{id}",
            Email = $"employee-{id}@example.invalid",
            RoleId = 1,
            HomeAddressId = id == 19 ? 201 : 101
        });
        db.SignatureImageFiles.Add(new SignatureImageFile { Id = 301, EmployeeId = 17, Bucket = "private", ObjectName = "synthetic.png" });
        await db.SaveChangesAsync();
        fixture.Authorities.Clear();
    }

    private async Task<string> SnapshotAsync(int? excludedAddressId = null)
    {
        await using var db = fixture.CreateContext();
        return JsonSerializer.Serialize(new
        {
            Employees = await db.Employees.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Addresses = await db.Addresses.AsNoTracking().Where(row => excludedAddressId == null || row.Id != excludedAddressId).OrderBy(row => row.Id).ToArrayAsync(),
            Roles = await db.Roles.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Signatures = await db.SignatureImageFiles.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync()
        });
    }
}

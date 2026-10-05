using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.EmployeeService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.EmployeeService.Tests;

[CollectionDefinition("Employee relationship rollback", DisableParallelization = true)]
public sealed class EmployeeRelationshipRollbackCollection;

[Collection("Employee relationship rollback")]
public sealed class EmployeeRelationshipRollbackHttpTests(EmployeeRouteAcceptanceFixture fixture)
    : IClassFixture<EmployeeRouteAcceptanceFixture>
{
    [Theory]
    [InlineData("addresses", "legacy-employee.addresses.delete")]
    [InlineData("roles", "legacy-employee.roles.delete")]
    public async Task LinkedMetadataDelete_LiveAllowedStillHonorsForeignKeyAndPreservesWholeEmployeeGraph(string kind, string permission)
    {
        await ResetAsync();
        var before = await SnapshotAsync();
        using var scope = fixture.Factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var cached = await cache.GetAsync("employee:1");
        using var client = fixture.Client(permission, $"/employees/{kind}/1");
        using var response = await client.DeleteAsync($"/employees/{kind}/1");
        await AssertPrivateFailureAsync(response);
        Assert.Equal(before, await SnapshotAsync());
        Assert.Equal(cached, await cache.GetAsync("employee:1"));
        var authority = Assert.Single(fixture.Authorities.Values);
        Assert.Equal(1, authority.LiveCalls);
    }

    [Theory]
    [InlineData("addresses", "legacy-employee.addresses.delete")]
    [InlineData("roles", "legacy-employee.roles.delete")]
    public async Task LinkedMetadataDelete_LiveDeniedRefusesBeforeDatabaseFailureDespiteSignedPermission(string kind, string permission)
    {
        await ResetAsync();
        var before = await SnapshotAsync();
        using var scope = fixture.Factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var cached = await cache.GetAsync("employee:1");
        using var client = fixture.Client(permission, $"/employees/{kind}/1", decision: "deny");
        using var response = await client.DeleteAsync($"/employees/{kind}/1");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("Original", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(before, await SnapshotAsync());
        Assert.Equal(cached, await cache.GetAsync("employee:1"));
        var authority = Assert.Single(fixture.Authorities.Values);
        Assert.Equal(1, authority.LiveCalls);
    }

    [Theory]
    [InlineData("address")]
    [InlineData("role")]
    public async Task MissingRelationship_CreateAndUpdateRejectAtomicallyWithoutEvictingStoredProjection(string kind)
    {
        await ResetAsync();
        var before = await SnapshotAsync();
        using var scope = fixture.Factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var cached = await cache.GetAsync("employee:1");
        var body = new
        {
            FirstName = "Rejected",
            LastName = "Relationship",
            Email = "rejected-employee@example.test",
            HomeAddressId = kind == "address" ? 999 : 1,
            RoleId = kind == "role" ? 999 : 1
        };
        using var creator = fixture.Client("legacy-employee.employees.create");
        using var updater = fixture.Client("legacy-employee.employees.update", "/employees/1");
        using var created = await creator.PostAsJsonAsync("/employees/", body);
        using var updated = await updater.PutAsJsonAsync("/employees/1", body);
        await AssertPrivateFailureAsync(created);
        await AssertPrivateFailureAsync(updated);
        Assert.Equal(before, await SnapshotAsync());
        Assert.Equal(cached, await cache.GetAsync("employee:1"));
        await using var db = fixture.CreateContext();
        Assert.Equal(1, await db.Employees.CountAsync());
        Assert.False(await db.Employees.AnyAsync(row => row.Email == "rejected-employee@example.test"));
    }

    private async Task ResetAsync()
    {
        await using var db = fixture.CreateContext();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"Employee\", \"Address\", \"Role\", \"SignatureImageFile\" RESTART IDENTITY CASCADE");
        var address = new Address { AddressLine1 = "Original road", CountryId = 764 };
        var role = new Role { Name = "Original role" };
        db.Employees.Add(new Employee
        {
            FirstName = "Original",
            LastName = "Employee",
            Email = "original-employee@example.test",
            HomeAddress = address,
            Role = role
        });
        await db.SaveChangesAsync();
        db.SignatureImageFiles.Add(new SignatureImageFile { EmployeeId = 1, Bucket = "fixture-only", ObjectName = "signature.png" });
        await db.SaveChangesAsync();
        fixture.Authorities.Clear();
        using var scope = fixture.Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IDistributedCache>().SetAsync("employee:1",
            Encoding.UTF8.GetBytes("""{"id":1,"firstName":"Original","lastName":"Employee","fullName":"Original Employee","email":"original-employee@example.test"}"""),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) });
    }

    private async Task<string> SnapshotAsync()
    {
        await using var db = fixture.CreateContext();
        return JsonSerializer.Serialize(new
        {
            Employee = await db.Employees.AsNoTracking().Select(row => new { row.Id, row.FirstName, row.LastName, row.Email, row.RoleId, row.HomeAddressId, row.ModifiedDate }).SingleAsync(),
            Address = await db.Addresses.AsNoTracking().Select(row => new { row.Id, row.AddressLine1, row.CountryId, row.ModifiedDate }).SingleAsync(),
            Role = await db.Roles.AsNoTracking().Select(row => new { row.Id, row.Name, row.ModifiedDate }).SingleAsync(),
            Signature = await db.SignatureImageFiles.AsNoTracking().Select(row => new { row.Id, row.EmployeeId, row.Bucket, row.ObjectName, row.ModifiedDate }).SingleAsync()
        });
    }

    private static async Task AssertPrivateFailureAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal(500, json.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("details").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("traceId").GetString()));
        foreach (var privateText in new[] { "Npgsql", "FK_Employee", "23503", "original-employee@example.test", "rejected-employee@example.test", "Original road", "Original role" })
            Assert.DoesNotContain(privateText, body, StringComparison.Ordinal);
    }
}

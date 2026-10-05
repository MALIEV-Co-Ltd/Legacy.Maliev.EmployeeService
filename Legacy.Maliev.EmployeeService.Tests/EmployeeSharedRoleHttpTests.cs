using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.EmployeeService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.EmployeeService.Tests;

[CollectionDefinition("Employee shared role", DisableParallelization = true)]
public sealed class EmployeeSharedRoleCollection;

[Collection("Employee shared role")]
public sealed class EmployeeSharedRoleHttpTests(EmployeeRouteAcceptanceFixture fixture) : IClassFixture<EmployeeRouteAcceptanceFixture>
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  Literal role  ")]
    [InlineData("วิศวกร")]
    public async Task SharedRoleUpdate_PreservesLiteralFieldsAndEmployeeRowsWhileInvalidatingEveryLinkedCache(string? value)
    {
        var graph = await SeedAsync();
        var before = await SnapshotAsync();
        using var scope = fixture.Factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var old = await PrimeCacheAsync(cache, graph.Employees);
        using var writer = fixture.Client("legacy-employee.roles.update", $"/employees/roles/{graph.RoleId}");
        using var updated = await writer.PutAsJsonAsync($"/employees/roles/{graph.RoleId}", new
        {
            Id = 999999,
            Name = value,
            Description = value,
            CreatedDate = new DateTime(1900, 1, 1)
        });
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
        foreach (var employee in graph.Employees.Where(row => row.RoleId == graph.RoleId))
            Assert.Null(await cache.GetAsync($"employee:{employee.Id}"));
        var unrelated = Assert.Single(graph.Employees, row => row.RoleId != graph.RoleId);
        Assert.Equal(old, await cache.GetAsync($"employee:{unrelated.Id}"));
        await using var db = fixture.CreateContext();
        var role = await db.Roles.AsNoTracking().SingleAsync(row => row.Id == graph.RoleId);
        Assert.Equal(value, role.Name);
        Assert.Equal(value, role.Description);
        Assert.Equal(graph.CreatedDate, role.CreatedDate);
        Assert.True(role.ModifiedDate > graph.ModifiedDate);
        Assert.Equal(before, await SnapshotAsync());
        using var reader = fixture.Client("legacy-employee.roles.read");
        using var detail = await reader.GetAsync($"/employees/roles/{graph.RoleId}/");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var detailJson = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        AssertRole(detailJson.RootElement, value);
        using var list = await reader.GetAsync("/employees/roles/");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var matching = Assert.Single(listJson.RootElement.EnumerateArray(), row => row.GetProperty("Id").GetInt32() == graph.RoleId);
        AssertRole(matching, value);
        foreach (var employee in graph.Employees.Where(row => row.RoleId == graph.RoleId))
        {
            using var employeeReader = fixture.Client("legacy-employee.employees.read", $"/employees/{employee.Id}");
            using var response = await employeeReader.GetAsync($"/employees/{employee.Id}/");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(employee.Id, json.RootElement.GetProperty("Id").GetInt32());
            Assert.Equal(employee.FullName, json.RootElement.GetProperty("FullName").GetString());
            Assert.Equal(graph.RoleId, json.RootElement.GetProperty("RoleId").GetInt32());
            Assert.False(json.RootElement.TryGetProperty("Role", out _));
        }
        Assert.Equal(old, await cache.GetAsync($"employee:{unrelated.Id}"));
        Assert.Equal(before, await SnapshotAsync());
    }

    [Theory]
    [InlineData("Name", false)]
    [InlineData("Name", true)]
    [InlineData("Description", false)]
    [InlineData("Description", true)]
    public async Task RoleTextLimit_FiftyCharactersPersistAndFiftyOneFailsPrivatelyWithoutGraphOrCacheChanges(string field, bool update)
    {
        var graph = await SeedAsync();
        var fifty = new string('ก', 50);
        using var creator = fixture.Client("legacy-employee.roles.create");
        using var created = await creator.PostAsJsonAsync("/employees/roles/", new { Name = fifty, Description = fifty });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        AssertRole(createdJson.RootElement, fifty);
        var id = createdJson.RootElement.GetProperty("Id").GetInt32();
        Assert.True(id > 0);
        Assert.EndsWith($"/Roles/{id}", created.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
        using var updater = fixture.Client("legacy-employee.roles.update", $"/employees/roles/{graph.RoleId}");
        using var valid = await updater.PutAsJsonAsync($"/employees/roles/{graph.RoleId}", new { Name = fifty, Description = fifty });
        Assert.Equal(HttpStatusCode.NoContent, valid.StatusCode);
        using var scope = fixture.Factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var old = await PrimeCacheAsync(cache, graph.Employees);
        var before = await FullSnapshotAsync();
        var payload = new { Name = field == "Name" ? fifty + "ก" : fifty, Description = field == "Description" ? fifty + "ก" : fifty };
        using var refused = update
            ? await updater.PutAsJsonAsync($"/employees/roles/{graph.RoleId}", payload)
            : await creator.PostAsJsonAsync("/employees/roles/", payload);
        Assert.Equal(HttpStatusCode.InternalServerError, refused.StatusCode);
        var body = await refused.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal(500, json.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("details").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("traceId").GetString()));
        Assert.DoesNotContain("Npgsql", body, StringComparison.Ordinal);
        Assert.DoesNotContain("22001", body, StringComparison.Ordinal);
        Assert.Equal(before, await FullSnapshotAsync());
        foreach (var employee in graph.Employees)
            Assert.Equal(old, await cache.GetAsync($"employee:{employee.Id}"));
    }

    private async Task<RoleGraph> SeedAsync()
    {
        await using var db = fixture.CreateContext();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"Employee\", \"Address\", \"Role\", \"SignatureImageFile\" RESTART IDENTITY CASCADE");
        fixture.Authorities.Clear();
        var role = new Role { Name = "Original role", Description = "Original description", CreatedDate = new DateTime(2020, 1, 1), ModifiedDate = new DateTime(2020, 1, 2) };
        var other = new Role { Name = "Unrelated role", Description = "Unrelated description" };
        db.Roles.AddRange(role, other);
        await db.SaveChangesAsync();
        db.Employees.AddRange(new Employee { FirstName = "First", LastName = "Fixture", Email = "first@example.test", RoleId = role.Id },
            new Employee { FirstName = "Second", LastName = "Fixture", Email = "second@example.test", RoleId = role.Id },
            new Employee { FirstName = "Unrelated", LastName = "Fixture", Email = "unrelated@example.test", RoleId = other.Id });
        await db.SaveChangesAsync();
        return new RoleGraph(role.Id, role.CreatedDate, role.ModifiedDate, await db.Employees.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync());
    }

    private static async Task<byte[]> PrimeCacheAsync(IDistributedCache cache, Employee[] employees)
    {
        var old = Encoding.UTF8.GetBytes("""{"id":999,"firstName":"Old cache","lastName":"Fixture","fullName":"Old cache Fixture","email":"old@example.test"}""");
        foreach (var employee in employees)
        {
            await cache.SetAsync($"employee:{employee.Id}", old, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) });
            Assert.Equal(old, await cache.GetAsync($"employee:{employee.Id}"));
        }
        return old;
    }

    private async Task<string> SnapshotAsync()
    {
        await using var db = fixture.CreateContext();
        return JsonSerializer.Serialize(new
        {
            Employees = await db.Employees.AsNoTracking().OrderBy(row => row.Id).Select(row => new { row.Id, row.FirstName, row.LastName, row.FullName, row.Email, row.PhoneNumber, row.DateOfBirth, row.RoleId, row.HomeAddressId, row.CreatedDate, row.ModifiedDate }).ToArrayAsync(),
            UnrelatedRoles = await db.Roles.AsNoTracking().Where(row => row.Name == "Unrelated role").Select(row => new { row.Id, row.Name, row.Description, row.CreatedDate, row.ModifiedDate }).ToArrayAsync(),
            Addresses = await db.Addresses.AsNoTracking().CountAsync(),
            Signatures = await db.SignatureImageFiles.AsNoTracking().CountAsync()
        });
    }

    private async Task<string> FullSnapshotAsync()
    {
        await using var db = fixture.CreateContext();
        return JsonSerializer.Serialize(new
        {
            OtherRows = await SnapshotAsync(),
            Roles = await db.Roles.AsNoTracking().OrderBy(row => row.Id).Select(row => new { row.Id, row.Name, row.Description, row.CreatedDate, row.ModifiedDate }).ToArrayAsync()
        });
    }

    private static void AssertRole(JsonElement role, string? value)
    {
        foreach (var field in new[] { "Name", "Description" })
        {
            if (value is null) Assert.False(role.TryGetProperty(field, out _));
            else Assert.Equal(value, role.GetProperty(field).GetString());
        }
        Assert.False(role.TryGetProperty("name", out _));
    }

    private sealed record RoleGraph(int RoleId, DateTime? CreatedDate, DateTime? ModifiedDate, Employee[] Employees);
}

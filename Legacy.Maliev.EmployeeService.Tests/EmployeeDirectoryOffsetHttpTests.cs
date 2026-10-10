using System.Net;
using System.Text.Json;
using Legacy.Maliev.EmployeeService.Data;
using Legacy.Maliev.EmployeeService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.EmployeeService.Tests;

public sealed class EmployeeDirectoryOffsetHttpTests(EmployeeDirectoryParityFixture fixture)
    : IClassFixture<EmployeeDirectoryParityFixture>
{
    [Theory]
    [InlineData(1073741825, 4)]
    [InlineData(536870913, 8)]
    [InlineData(int.MaxValue, 250)]
    [InlineData(int.MaxValue, 1)]
    [InlineData(3, 4)]
    public async Task ExhaustedPage_ReturnsNotFoundWithoutWrappingOrMutatingData(int index, int size)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var cancellation = lifetime.Token;
        await fixture.SeedAsync();
        int[] allIds;
        int[] filteredIds;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EmployeeDbContext>();
            var address = new Address { AddressLine1 = "Synthetic boundary road", CountryId = 764 };
            var role = new Role { Name = "Boundary role", Description = "Synthetic role" };
            var matches = Enumerable.Range(0, 5).Select(value => new Employee
            {
                FirstName = "Boundary",
                LastName = "Staff",
                Email = $"boundary-{value}@example.invalid",
                HomeAddress = address,
                Role = role,
            }).ToArray();
            db.Employees.AddRange(matches);
            db.Employees.Add(new Employee
            {
                FirstName = "Other",
                LastName = "Staff",
                Email = "other@example.invalid",
            });
            await db.SaveChangesAsync(cancellation);
            filteredIds = matches.Select(row => row.Id).Order().ToArray();
            allIds = await db.Employees.OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(cancellation);
            db.SignatureImageFiles.Add(new SignatureImageFile
            {
                EmployeeId = filteredIds[0],
                Bucket = "synthetic-bucket",
                ObjectName = "synthetic-object",
            });
            await db.SaveChangesAsync(cancellation);
            Assert.Equal(6, allIds.Length);
            Assert.Equal(5, filteredIds.Length);
        }

        var before = await SnapshotAsync(cancellation);
        using var client = fixture.CreateClient();
        await AssertOrdinaryPagesAsync(client, "", allIds, cancellation);
        await AssertOrdinaryPagesAsync(client, "search=Boundary&", filteredIds, cancellation);
        foreach (var query in new[] { "", "search=Boundary&" })
        {
            using var exhausted = await client.GetAsync($"/Employees?{query}index={index}&size={size}", cancellation);
            Assert.Equal(HttpStatusCode.NotFound, exhausted.StatusCode);
        }

        await AssertOrdinaryPagesAsync(client, "", allIds, cancellation);
        await AssertOrdinaryPagesAsync(client, "search=Boundary&", filteredIds, cancellation);
        Assert.Equal(before, await SnapshotAsync(cancellation));
    }

    private static async Task AssertOrdinaryPagesAsync(HttpClient client, string query,
        int[] ids, CancellationToken cancellation)
    {
        for (var page = 1; page <= 2; page++)
        {
            using var response = await client.GetAsync($"/Employees?{query}index={page}&size=4", cancellation);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
            var root = json.RootElement;
            string[] keys = ["Items", "PageIndex", "TotalPages", "TotalRecords", "HasNextPage", "HasPreviousPage"];
            Assert.Equal(keys.Order(StringComparer.Ordinal), root.EnumerateObject().Select(row => row.Name).Order(StringComparer.Ordinal));
            Assert.Equal(ids.Length, root.GetProperty("TotalRecords").GetInt32());
            Assert.Equal(2, root.GetProperty("TotalPages").GetInt32());
            Assert.Equal(page, root.GetProperty("PageIndex").GetInt32());
            Assert.Equal(page < 2, root.GetProperty("HasNextPage").GetBoolean());
            Assert.Equal(page > 1, root.GetProperty("HasPreviousPage").GetBoolean());
            var items = root.GetProperty("Items").EnumerateArray().ToArray();
            Assert.Equal(ids.Skip((page - 1) * 4).Take(4).ToArray(),
                items.Select(row => row.GetProperty("Id").GetInt32()).ToArray());
            foreach (var item in items)
            {
                Assert.False(item.TryGetProperty("EmployeeIdentity", out _));
                Assert.False(item.TryGetProperty("PasswordHash", out _));
                Assert.False(item.TryGetProperty("SecurityStamp", out _));
                Assert.False(item.TryGetProperty("SignatureImageFile", out _));
                Assert.False(item.TryGetProperty("Role", out _));
            }
        }
    }

    private async Task<string> SnapshotAsync(CancellationToken cancellation)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeeDbContext>();
        return JsonSerializer.Serialize(new
        {
            Employees = await db.Employees.AsNoTracking().OrderBy(row => row.Id).Select(row => new
            {
                row.Id,
                row.RoleId,
                row.FirstName,
                row.LastName,
                row.FullName,
                row.PhoneNumber,
                row.Email,
                row.DateOfBirth,
                row.HomeAddressId,
                row.CreatedDate,
                row.ModifiedDate,
            }).ToArrayAsync(cancellation),
            Addresses = await db.Addresses.AsNoTracking().OrderBy(row => row.Id).Select(row => new
            {
                row.Id,
                row.Building,
                row.AddressLine1,
                row.AddressLine2,
                row.City,
                row.State,
                row.PostalCode,
                row.CountryId,
                row.CreatedDate,
                row.ModifiedDate,
            }).ToArrayAsync(cancellation),
            Roles = await db.Roles.AsNoTracking().OrderBy(row => row.Id).Select(row => new
            {
                row.Id,
                row.Name,
                row.Description,
                row.CreatedDate,
                row.ModifiedDate,
            }).ToArrayAsync(cancellation),
            Signatures = await db.SignatureImageFiles.AsNoTracking().OrderBy(row => row.Id).Select(row => new
            {
                row.Id,
                row.EmployeeId,
                row.Bucket,
                row.ObjectName,
                row.CreatedDate,
                row.ModifiedDate,
            }).ToArrayAsync(cancellation),
            EmployeeRevisions = await db.Database.SqlQueryRaw<string>(
                "SELECT \"ID\"::text || ':' || xmin::text AS \"Value\" FROM \"Employee\" ORDER BY \"ID\"").ToArrayAsync(cancellation),
        });
    }
}

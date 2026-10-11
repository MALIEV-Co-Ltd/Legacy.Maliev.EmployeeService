using System.Net;
using System.Text.Json;
using Legacy.Maliev.EmployeeService.Data;
using Legacy.Maliev.EmployeeService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Legacy.Maliev.EmployeeService.Tests;

public sealed class EmployeeDefaultPageHttpTests(EmployeeDirectoryParityFixture fixture)
    : IClassFixture<EmployeeDirectoryParityFixture>
{
    private const string Needle = "employee-default-match";

    [Theory]
    [InlineData(60, 100)]
    [InlineData(100, 900)]
    [InlineData(300, 900)]
    public async Task OmittedOrEmptySize_ReturnsEntireFilteredCohortAndPreservesExplicitPages(
        int matches, int distractors)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var cancellation = lifetime.Token;
        await fixture.SeedAsync();
        Assert.Equal("Production", fixture.Factory.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);
        int[] matchingIds;
        int[] allIds;
        int roleId;
        int addressId;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EmployeeDbContext>();
            var role = new Role { Name = "Shared directory role", Description = "Synthetic role" };
            var address = new Address { AddressLine1 = "Synthetic shared road", CountryId = 764 };
            var cohort = Enumerable.Range(0, matches).Select(value => new Employee
            {
                FirstName = Needle, LastName = "Staff", Email = $"match-{value}@example.invalid",
                Role = role, HomeAddress = address,
            }).ToArray();
            db.Employees.AddRange(cohort);
            db.Employees.AddRange(Enumerable.Range(0, distractors).Select(value => new Employee
            {
                FirstName = "Distractor", LastName = "Staff", Email = $"distractor-{value}@example.invalid",
            }));
            await db.SaveChangesAsync(cancellation);
            matchingIds = cohort.Select(row => row.Id).Order().ToArray();
            allIds = await db.Employees.OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(cancellation);
            roleId = role.Id;
            addressId = address.Id;
            db.SignatureImageFiles.Add(new SignatureImageFile
            {
                EmployeeId = matchingIds[0], Bucket = "synthetic-bucket", ObjectName = "synthetic-object",
            });
            await db.SaveChangesAsync(cancellation);
            Assert.Equal(matches + distractors, allIds.Length);
        }

        var before = await SnapshotAsync(cancellation);
        using var client = fixture.CreateClient();
        foreach (var (query, ids) in new[]
        {
            ("/Employees?", allIds),
            ("/Employees?search=" + Uri.EscapeDataString(Needle) + "&", matchingIds),
        })
        {
            foreach (var suffix in new[] { "", "size=", "index=1", "index=1&size=" })
            {
                var actual = await AssertPageAsync(client, query + suffix, ids,
                    matchingIds, roleId, addressId, 1, ids.Length, cancellation);
                Assert.Equal(ids, actual);
                Assert.Equal(ids.Length, actual.Distinct().Count());
            }
            foreach (var suffix in new[] { "index=2", "index=2&size=" })
            {
                using var exhausted = await client.GetAsync(query + suffix, cancellation);
                Assert.Equal(HttpStatusCode.NotFound, exhausted.StatusCode);
            }
            foreach (var suffix in new[] { "size=0", "index=0&size=0", "index=2&size=0" })
            {
                using var empty = await client.GetAsync(query + suffix, cancellation);
                Assert.Equal(HttpStatusCode.NotFound, empty.StatusCode);
            }
            foreach (var size in new[] { 251, 300, 10_000, int.MaxValue })
                await AssertPageAsync(client, query + $"size={size}", ids,
                    matchingIds, roleId, addressId, 1, size, cancellation);
            await AssertPageAsync(client, query + "index=0&size=251", ids,
                matchingIds, roleId, addressId, 1, 251, cancellation);
            if (ids.Length > 251)
                await AssertPageAsync(client, query + "index=2&size=251", ids,
                    matchingIds, roleId, addressId, 2, 251, cancellation);
            foreach (var suffix in new[] { "index=0", "index=-1", "index=0&size=" })
                await AssertPageAsync(client, query + suffix, ids,
                    matchingIds, roleId, addressId, 1, ids.Length, cancellation);
            foreach (var suffix in new[] { "size=-1", "index=2&size=-1", "size=-2147483648" })
            {
                using var rejected = await client.GetAsync(query + suffix, cancellation);
                Assert.Equal(HttpStatusCode.InternalServerError, rejected.StatusCode);
            }
            using var malformed = await client.GetAsync(query + "size=not-an-integer", cancellation);
            Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
            var explicitPages = (int)Math.Ceiling(ids.Length / 25d);
            await AssertPageAsync(client, query + "index=1&size=25", ids,
                matchingIds, roleId, addressId, 1, 25, cancellation);
            await AssertPageAsync(client, query + $"index={explicitPages}&size=25", ids,
                matchingIds, roleId, addressId, explicitPages, 25, cancellation);
            using var explicitExhausted = await client.GetAsync(query + $"index={explicitPages + 1}&size=25", cancellation);
            Assert.Equal(HttpStatusCode.NotFound, explicitExhausted.StatusCode);
        }

        using var noMatch = await client.GetAsync("/Employees?search=absent-employee-default", cancellation);
        Assert.Equal(HttpStatusCode.NotFound, noMatch.StatusCode);
        using var emptyNoMatch = await client.GetAsync("/Employees?search=absent-employee-default&size=", cancellation);
        Assert.Equal(HttpStatusCode.NotFound, emptyNoMatch.StatusCode);
        foreach (var suffix in new[] { "&size=0", "&size=-1", "&size=10000" })
        {
            using var noRows = await client.GetAsync("/Employees?search=absent-employee-default" + suffix, cancellation);
            Assert.Equal(HttpStatusCode.NotFound, noRows.StatusCode);
        }
        Assert.Equal(before, await SnapshotAsync(cancellation));
    }

    [Theory]
    [InlineData("anonymous", HttpStatusCode.Unauthorized)]
    [InlineData("missing-permission", HttpStatusCode.Forbidden)]
    [InlineData("wrong-signature", HttpStatusCode.Unauthorized)]
    [InlineData("expired", HttpStatusCode.Unauthorized)]
    public async Task OmittedSize_StillRequiresEmployeeDirectoryAuthority(string authority, HttpStatusCode expected)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var cancellation = lifetime.Token;
        await fixture.SeedAsync(new DirectoryRow(1, "Synthetic", "Staff", "staff@example.invalid", Address: true));
        var before = await SnapshotAsync(cancellation);
        using var client = fixture.CreateClient(authority);
        foreach (var path in new[] { "/Employees", "/Employees?size=", "/Employees?search=Synthetic", "/Employees?search=Synthetic&size=", "/Employees?size=25", "/Employees?size=10000", "/Employees?size=0", "/Employees?size=-1" })
        {
            using var response = await client.GetAsync(path, cancellation);
            Assert.Equal(expected, response.StatusCode);
        }
        Assert.Equal(before, await SnapshotAsync(cancellation));
    }

    private static async Task<int[]> AssertPageAsync(HttpClient client, string path, int[] ids,
        int[] matchingIds, int roleId, int addressId, int page, int size, CancellationToken cancellation)
    {
        using var response = await client.GetAsync(path, cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
        var root = json.RootElement;
        string[] keys = ["Items", "PageIndex", "TotalPages", "TotalRecords", "HasNextPage", "HasPreviousPage"];
        Assert.Equal(keys.Order(StringComparer.Ordinal), root.EnumerateObject().Select(row => row.Name).Order(StringComparer.Ordinal));
        var pages = (int)Math.Ceiling(ids.Length / (double)size);
        Assert.Equal(ids.Length, root.GetProperty("TotalRecords").GetInt32());
        Assert.Equal(pages, root.GetProperty("TotalPages").GetInt32());
        Assert.Equal(page, root.GetProperty("PageIndex").GetInt32());
        Assert.Equal(page < pages, root.GetProperty("HasNextPage").GetBoolean());
        Assert.Equal(page > 1, root.GetProperty("HasPreviousPage").GetBoolean());
        var items = root.GetProperty("Items").EnumerateArray().ToArray();
        var actualIds = items.Select(row => row.GetProperty("Id").GetInt32()).ToArray();
        Assert.Equal(ids.Skip((page - 1) * size).Take(size).ToArray(), actualIds);
        foreach (var item in items)
        {
            Assert.False(item.TryGetProperty("EmployeeIdentity", out _));
            Assert.False(item.TryGetProperty("PasswordHash", out _));
            Assert.False(item.TryGetProperty("SecurityStamp", out _));
            Assert.False(item.TryGetProperty("SignatureImageFile", out _));
            Assert.False(item.TryGetProperty("Role", out _));
            if (matchingIds.Contains(item.GetProperty("Id").GetInt32()))
            {
                Assert.Equal(roleId, item.GetProperty("RoleId").GetInt32());
                Assert.Equal(addressId, item.GetProperty("HomeAddressId").GetInt32());
                Assert.Equal(addressId, item.GetProperty("HomeAddress").GetProperty("Id").GetInt32());
            }
            else
            {
                Assert.False(item.TryGetProperty("RoleId", out _));
                Assert.False(item.TryGetProperty("HomeAddress", out _));
                Assert.False(item.TryGetProperty("HomeAddressId", out _));
            }
        }
        return actualIds;
    }

    private async Task<string> SnapshotAsync(CancellationToken cancellation)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeeDbContext>();
        return JsonSerializer.Serialize(new
        {
            Employees = await db.Employees.AsNoTracking().OrderBy(row => row.Id).Select(row => new
            {
                row.Id, row.RoleId, row.FirstName, row.LastName, row.FullName, row.PhoneNumber, row.Email,
                row.DateOfBirth, row.HomeAddressId, row.CreatedDate, row.ModifiedDate,
            }).ToArrayAsync(cancellation),
            Addresses = await db.Addresses.AsNoTracking().OrderBy(row => row.Id).Select(row => new
            {
                row.Id, row.Building, row.AddressLine1, row.AddressLine2, row.City, row.State,
                row.PostalCode, row.CountryId, row.CreatedDate, row.ModifiedDate,
            }).ToArrayAsync(cancellation),
            Roles = await db.Roles.AsNoTracking().OrderBy(row => row.Id).Select(row => new
            {
                row.Id, row.Name, row.Description, row.CreatedDate, row.ModifiedDate,
            }).ToArrayAsync(cancellation),
            Signatures = await db.SignatureImageFiles.AsNoTracking().OrderBy(row => row.Id).Select(row => new
            {
                row.Id, row.EmployeeId, row.Bucket, row.ObjectName, row.CreatedDate, row.ModifiedDate,
            }).ToArrayAsync(cancellation),
            EmployeeRevisions = await db.Database.SqlQueryRaw<string>(
                "SELECT \"ID\"::text || ':' || xmin::text AS \"Value\" FROM \"Employee\" ORDER BY \"ID\"").ToArrayAsync(cancellation),
        });
    }
}

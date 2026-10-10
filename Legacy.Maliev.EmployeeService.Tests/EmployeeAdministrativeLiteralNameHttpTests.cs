using System.Net;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.EmployeeService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.EmployeeService.Tests;

[Collection("Employee administrative profile")]
public sealed class EmployeeAdministrativeLiteralNameHttpTests(EmployeeRouteAcceptanceFixture fixture)
    : IClassFixture<EmployeeRouteAcceptanceFixture>
{
    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("versioned")]
    public async Task AdministrativeNames_PreserveLiteralValuesOrRefuseBeforeMutation(string route)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var token = timeout.Token;
        await SeedAsync(token);
        await using var db = fixture.CreateContext();
        Assert.Equal("UTF8", await db.Database.SqlQueryRaw<string>("SELECT current_setting('server_encoding') AS \"Value\"").SingleAsync(token));
        Assert.Equal(2, await db.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM information_schema.columns WHERE table_name = 'Employee' AND column_name IN ('FirstName', 'LastName') AND character_maximum_length = 256").SingleAsync(token));
        using var scope = fixture.Factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var otherCache = Encoding.UTF8.GetBytes("independent unrelated employee cache sentinel");
        await cache.SetAsync("employee:2", otherCache, new DistributedCacheEntryOptions(), token);
        var create = route == "create";
        using var writer = fixture.Client(create ? "legacy-employee.employees.create" : "legacy-employee.employees.update", create ? "global" : "/employees/1");
        using var reader = fixture.Client("legacy-employee.employees.read", "/employees/1");
        var cases = new (string Label, string Value, int Scalars, bool Accepted, string? RawEscape)[]
        {
            ("EnglishLiteral", " \tAlice\u00a0 ", 9, true, null),
            ("ThaiLiteral", " \tวิศวกร\u00a0 ", 10, true, null),
            ("English256", new string('A', 256), 256, true, null),
            ("English257", new string('A', 257), 257, false, null),
            ("Thai256", new string('ก', 256), 256, true, null),
            ("Thai257", new string('ก', 257), 257, false, null),
            ("Supplementary256", string.Concat(Enumerable.Repeat("\U0001f600", 256)), 256, false, null),
            ("Supplementary257", string.Concat(Enumerable.Repeat("\U0001f600", 257)), 257, false, null),
            ("Supplementary128", string.Concat(Enumerable.Repeat("\U0001f600", 128)), 128, true, null),
            ("Supplementary129", string.Concat(Enumerable.Repeat("\U0001f600", 129)), 129, false, null),
            ("MixedThaiSupplementary256Units", new string('ก', 254) + "\U0001f600", 255, true, null),
            ("MixedThaiSupplementary257Units", new string('ก', 255) + "\U0001f600", 256, false, null),
            ("Combining256", string.Concat(Enumerable.Repeat("ก\u0e49", 128)), 256, true, null),
            ("Combining257", string.Concat(Enumerable.Repeat("ก\u0e49", 128)) + "\u0e49", 257, false, null),
            ("Padded256", " \t\u00a0" + new string('ก', 250) + "\u00a0\t ", 256, true, null),
            ("Padded257", " \t\u00a0" + new string('ก', 251) + "\u00a0\t ", 257, false, null),
            ("TrailingSpace257", new string('ก', 256) + " ", 257, false, null),
            ("Nul", "Name\0", 5, false, null),
            ("HighSurrogate", "__invalid_utf16__", 0, false, "\\ud800"),
            ("LowSurrogate", "__invalid_utf16__", 0, false, "\\udc00"),
            ("Blank", " \t\u00a0 ", 4, false, null),
        };
        foreach (var field in new[] { "FirstName", "LastName" })
            foreach (var item in cases)
            {
                if (item.RawEscape is null && item.Label != "Nul")
                    Assert.Equal(item.Scalars, await db.Database.SqlQuery<int>($"SELECT char_length({item.Value}) AS \"Value\"").SingleAsync(token));
                var ownerCache = Encoding.UTF8.GetBytes("independent owner cache sentinel");
                await cache.SetAsync("employee:1", ownerCache, new DistributedCacheEntryOptions(), token);
                var before = await SnapshotAsync(0, token);
                var unrelated = await SnapshotAsync(create ? 0 : 1, token);
                var originalCreated = await db.Employees.Where(row => row.Id == 1).Select(row => row.CreatedDate).SingleAsync(token);
                using var edit = await reader.GetAsync("/employees/1/edit", token);
                Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
                using var editJson = JsonDocument.Parse(await edit.Content.ReadAsStringAsync(token));
                Assert.Equal(1, editJson.RootElement.GetProperty("RoleId").GetInt32());
                Assert.Equal(1, editJson.RootElement.GetProperty("HomeAddressId").GetInt32());
                Assert.False(editJson.RootElement.TryGetProperty("Role", out _));
                Assert.False(editJson.RootElement.TryGetProperty("HomeAddress", out _));
                var version = edit.Headers.ETag!.ToString();
                var first = field == "FirstName" ? item.Value : "วิศวกร";
                var last = field == "LastName" ? item.Value : "ใจดี";
                var path = create ? "/employees/" : route == "update" ? "/employees/1/" : "/employees/1/versioned";
                using var request = Request(create ? HttpMethod.Post : HttpMethod.Put, path, first, last, item.RawEscape);
                if (route == "versioned") request.Headers.TryAddWithoutValidation("If-Match", version);
                using var result = await writer.SendAsync(request, token);
                Assert.True(result.StatusCode == (item.Accepted ? create ? HttpStatusCode.Created : HttpStatusCode.NoContent : HttpStatusCode.BadRequest),
                    $"{route}/{field}/{item.Label}: actual {result.StatusCode}");
                if (!item.Accepted)
                {
                    Assert.Equal(before, await SnapshotAsync(0, token));
                    Assert.Equal(ownerCache, await cache.GetAsync("employee:1", token));
                    Assert.Equal(otherCache, await cache.GetAsync("employee:2", token));
                    continue;
                }
                var id = 1;
                if (create)
                {
                    using var body = JsonDocument.Parse(await result.Content.ReadAsStringAsync(token));
                    id = body.RootElement.GetProperty("Id").GetInt32();
                    AssertLiteral(body.RootElement, first, last);
                    Assert.EndsWith($"/employees/{id}", result.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
                    Assert.Equal(ownerCache, await cache.GetAsync("employee:1", token));
                }
                else Assert.Null(await cache.GetAsync("employee:1", token));
                Assert.Equal(otherCache, await cache.GetAsync("employee:2", token));
                var stored = await db.Employees.AsNoTracking().SingleAsync(row => row.Id == id, token);
                Assert.Equal(first, stored.FirstName); Assert.Equal(last, stored.LastName);
                Assert.Equal((first + " " + last).Trim(' '), stored.FullName);
                Assert.Equal("literal@example.test", stored.Email);
                Assert.Equal(" \t0800000000\u00a0 ", stored.PhoneNumber);
                Assert.Equal(1, stored.RoleId); Assert.Equal(1, stored.HomeAddressId);
                Assert.Equal(new DateTime(1980, 1, 2), stored.DateOfBirth);
                if (!create) Assert.Equal(originalCreated, stored.CreatedDate);
                Assert.Equal(unrelated, await SnapshotAsync(id, token));
                using var specificReader = fixture.Client("legacy-employee.employees.read", $"/employees/{id}");
                using var read = await specificReader.GetAsync($"/employees/{id}", token);
                Assert.Equal(HttpStatusCode.OK, read.StatusCode);
                using var json = JsonDocument.Parse(await read.Content.ReadAsStringAsync(token));
                AssertLiteral(json.RootElement, first, last);
                if (route == "versioned")
                {
                    using var refreshed = await reader.GetAsync("/employees/1/edit", token);
                    Assert.NotEqual(version, refreshed.Headers.ETag!.ToString());
                    Assert.Equal(result.Headers.ETag!.ToString(), refreshed.Headers.ETag!.ToString());
                    await cache.SetAsync("employee:1", ownerCache, new DistributedCacheEntryOptions(), token);
                    var after = await SnapshotAsync(0, token);
                    using var staleRequest = Request(HttpMethod.Put, path, " Stale ", " Writer ", null);
                    staleRequest.Headers.TryAddWithoutValidation("If-Match", version);
                    using var stale = await writer.SendAsync(staleRequest, token);
                    Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
                    Assert.Equal(after, await SnapshotAsync(0, token));
                    Assert.Equal(ownerCache, await cache.GetAsync("employee:1", token));
                    Assert.Equal(otherCache, await cache.GetAsync("employee:2", token));
                }
            }
        fixture.IamTransportEvidence.AssertHealthy();
    }

    [Theory]
    [InlineData("anonymous", HttpStatusCode.Unauthorized)]
    [InlineData("wrong-signature", HttpStatusCode.Unauthorized)]
    [InlineData("expired", HttpStatusCode.Unauthorized)]
    [InlineData("missing-permission", HttpStatusCode.Forbidden)]
    public async Task LiteralNames_DoNotBypassAdministrativeAuthority(string authority, HttpStatusCode status)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var token = timeout.Token;
        await SeedAsync(token);
        using var scope = fixture.Factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var sentinel = Encoding.UTF8.GetBytes("independent cache sentinel");
        await cache.SetAsync("employee:1", sentinel, new DistributedCacheEntryOptions(), token);
        var before = await SnapshotAsync(0, token);
        foreach (var route in new[] { "create", "update", "versioned" })
        {
            var create = route == "create";
            using var client = fixture.Client(create ? "legacy-employee.employees.create" : "legacy-employee.employees.update",
                create ? "global" : "/employees/1", authority);
            using var request = Request(create ? HttpMethod.Post : HttpMethod.Put,
                create ? "/employees/" : route == "update" ? "/employees/1" : "/employees/1/versioned", " Alice ", " ใจดี ", null);
            using var result = await client.SendAsync(request, token);
            Assert.Equal(status, result.StatusCode);
        }
        Assert.Equal(before, await SnapshotAsync(0, token));
        Assert.Equal(sentinel, await cache.GetAsync("employee:1", token));
        fixture.IamTransportEvidence.AssertHealthy();
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string first, string last, string? rawEscape)
    {
        var json = JsonSerializer.Serialize(new
        {
            FirstName = first,
            LastName = last,
            Email = " literal@example.test ",
            PhoneNumber = " \t0800000000\u00a0 ",
            DateOfBirth = new DateTime(1980, 1, 2),
            RoleId = 1,
            HomeAddressId = 1,
        });
        if (rawEscape is not null) json = json.Replace("\"__invalid_utf16__\"", "\"" + rawEscape + "\"", StringComparison.Ordinal);
        return new HttpRequestMessage(method, path) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private static void AssertLiteral(JsonElement item, string first, string last)
    {
        Assert.Equal(first, item.GetProperty("FirstName").GetString());
        Assert.Equal(last, item.GetProperty("LastName").GetString());
        Assert.Equal((first + " " + last).Trim(' '), item.GetProperty("FullName").GetString());
        Assert.False(item.TryGetProperty("firstName", out _));
        Assert.Equal(1, item.GetProperty("RoleId").GetInt32());
        Assert.Equal(1, item.GetProperty("HomeAddressId").GetInt32());
        var address = item.GetProperty("HomeAddress");
        Assert.Equal(1, address.GetProperty("Id").GetInt32());
        Assert.Equal("Shared road", address.GetProperty("AddressLine1").GetString());
        Assert.Equal(764, address.GetProperty("CountryId").GetInt32());
        // Exact pinned ordinary Project supplies null for Role; it preserves RoleId.
        Assert.False(item.TryGetProperty("Role", out _));
        foreach (var field in new[] { "PasswordHash", "SecurityStamp", "Token" }) Assert.False(item.TryGetProperty(field, out _));
    }

    private async Task SeedAsync(CancellationToken token)
    {
        await using var db = fixture.CreateContext();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"Employee\", \"Address\", \"Role\", \"SignatureImageFile\" RESTART IDENTITY CASCADE", token);
        db.Addresses.Add(new Address { AddressLine1 = "Shared road", CountryId = 764 });
        db.Roles.Add(new Role { Name = "Shared role", Description = "Shared role description" });
        await db.SaveChangesAsync(token);
        foreach (var first in new[] { "Before", "Unrelated" })
            db.Employees.Add(new Employee
            {
                FirstName = first,
                LastName = "Employee",
                Email = first.ToLowerInvariant() + "@example.test",
                PhoneNumber = "020000001",
                HomeAddressId = 1,
                RoleId = 1,
                CreatedDate = new DateTime(2020, 1, 1),
                ModifiedDate = new DateTime(2020, 1, 1),
            });
        await db.SaveChangesAsync(token);
        db.SignatureImageFiles.Add(new SignatureImageFile { EmployeeId = 1, Bucket = "fixture-bucket", ObjectName = "literal+%?.png" });
        await db.SaveChangesAsync(token);
    }

    private async Task<string> SnapshotAsync(int excludeEmployee, CancellationToken token)
    {
        await using var db = fixture.CreateContext();
        return await db.Database.SqlQuery<string>($"""
            SELECT jsonb_build_object(
              'employees', (SELECT jsonb_agg(to_jsonb(e) || jsonb_build_object('xmin', e.xmin::text) ORDER BY e."ID") FROM "Employee" e WHERE e."ID" <> {excludeEmployee}),
              'addresses', (SELECT jsonb_agg(to_jsonb(a) || jsonb_build_object('xmin', a.xmin::text) ORDER BY a."ID") FROM "Address" a),
              'roles', (SELECT jsonb_agg(to_jsonb(r) || jsonb_build_object('xmin', r.xmin::text) ORDER BY r."ID") FROM "Role" r),
              'signatures', (SELECT jsonb_agg(to_jsonb(s) || jsonb_build_object('xmin', s.xmin::text) ORDER BY s."ID") FROM "SignatureImageFile" s)
            )::text AS "Value"
            """).SingleAsync(token);
    }
}

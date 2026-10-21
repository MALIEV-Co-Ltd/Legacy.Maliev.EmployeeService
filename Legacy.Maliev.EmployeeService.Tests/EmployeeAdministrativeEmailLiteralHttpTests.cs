using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.EmployeeService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.EmployeeService.Tests;

/// <summary>Checks literal administrative email storage, capacity failures, authorization, conditional versions, and cache preservation.</summary>
/// <param name="fixture">The shared route fixture providing PostgreSQL, Redis, signed HTTP clients, and controlled IAM transport for administrative persistence assertions.</param>
[Collection("Employee administrative persistence")]
public sealed class EmployeeAdministrativeEmailLiteralHttpTests(EmployeeRouteAcceptanceFixture fixture)
    : IClassFixture<EmployeeRouteAcceptanceFixture>
{
    /// <summary>Checks that email padding survives employee creation or update while unrelated graph rows and caches remain intact.</summary>
    /// <param name="update">Whether to update employee 17 with PUT instead of creating an employee with POST.</param>
    /// <param name="email">The literal padded email address expected unchanged in persistence and HTTP readback.</param>
    /// <returns>A task that completes after asserting literal email storage, response casing, timestamp behavior, and scoped cache and graph changes.</returns>
    [Theory]
    [InlineData(false, "  synthetic@example.invalid  ")]
    [InlineData(true, "  synthetic@example.invalid  ")]
    [InlineData(false, "\tsynthetic@example.invalid\t")]
    [InlineData(true, "\tsynthetic@example.invalid\t")]
    [InlineData(false, "\u00a0ช่าง@example.invalid\u00a0")]
    [InlineData(true, "\u00a0ช่าง@example.invalid\u00a0")]
    [InlineData(false, "\u2003synthetic@example.invalid\u2003")]
    [InlineData(true, "\u2003synthetic@example.invalid\u2003")]
    public async Task AdministrativeEmail_PostAndPutPreserveLiteralPaddingAndUnrelatedGraph(bool update, string email)
    {
        await SeedAsync();
        using var scope = fixture.Factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var ownerCache = await PrimeAsync(cache, 17);
        var otherCache = await PrimeAsync(cache, 18);
        var before = await SnapshotAsync(update ? 17 : null);
        var beforeWrite = DateTime.UtcNow;
        using var writer = Writer(update);
        using var response = await SendAsync(writer, update, email);
        var afterWrite = DateTime.UtcNow;
        Assert.Equal(update ? HttpStatusCode.NoContent : HttpStatusCode.Created, response.StatusCode);
        var id = 17;
        if (update)
        {
            Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
            Assert.Null(await cache.GetAsync("employee:17"));
        }
        else
        {
            using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            id = created.RootElement.GetProperty("Id").GetInt32();
            Assert.NotEqual(999999, id);
            Assert.Equal(email, created.RootElement.GetProperty("Email").GetString());
            Assert.EndsWith($"/Employees/{id}", response.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(ownerCache, await cache.GetAsync("employee:17"));
        }
        Assert.Equal(otherCache, await cache.GetAsync("employee:18"));
        Assert.Equal(before, await SnapshotAsync(id));
        await using var db = fixture.CreateContext();
        var saved = await db.Employees.AsNoTracking().SingleAsync(row => row.Id == id);
        Assert.Equal(email, saved.Email);
        Assert.Equal("Owner", saved.FirstName);
        Assert.Equal("Synthetic", saved.LastName);
        Assert.Equal("Owner Synthetic", saved.FullName);
        Assert.Equal("0900000000", saved.PhoneNumber);
        Assert.Equal(1, saved.RoleId);
        Assert.Equal(1, saved.HomeAddressId);
        Assert.Equal(new DateTime(1980, 1, 2), saved.DateOfBirth);
        Assert.NotNull(saved.ModifiedDate);
        Assert.InRange(saved.ModifiedDate.Value, beforeWrite.AddSeconds(-1), afterWrite.AddSeconds(1));
        if (update) Assert.Equal(new DateTime(2020, 1, 1), saved.CreatedDate);
        else
        {
            Assert.NotNull(saved.CreatedDate);
            Assert.InRange(saved.CreatedDate.Value, beforeWrite.AddSeconds(-1), afterWrite.AddSeconds(1));
        }
        using var reader = fixture.Client("legacy-employee.employees.read", $"/employees/{id}");
        using var read = await reader.GetAsync($"/employees/{id}/");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var json = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.Equal(email, json.RootElement.GetProperty("Email").GetString());
        Assert.False(json.RootElement.TryGetProperty("email", out _));
        fixture.IamTransportEvidence.AssertHealthy();
    }

    /// <summary>Checks that a conditional email update preserves literal text and returns a version usable by a subsequent update.</summary>
    /// <param name="email">The literal padded email address sent with the current employee precondition.</param>
    /// <returns>A task that completes after asserting the stored email, renewed reusable ETag, and targeted cache eviction.</returns>
    [Theory]
    [InlineData("  synthetic@example.invalid  ")]
    [InlineData("\tsynthetic@example.invalid\t")]
    [InlineData("\u00a0ช่าง@example.invalid\u00a0")]
    [InlineData("\u2003synthetic@example.invalid\u2003")]
    public async Task ConditionalAdministrativeEmail_PreservesLiteralValueAndReturnsReusableVersion(string email)
    {
        await SeedAsync();
        using var scope = fixture.Factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var otherCache = await PrimeAsync(cache, 18);
        var before = await SnapshotAsync(17);
        var version = await VersionAsync();
        await PrimeAsync(cache, 17);
        using var writer = Writer(true);
        using var response = await SendAsync(writer, true, email, version);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
        Assert.NotNull(response.Headers.ETag);
        Assert.NotEqual(version, response.Headers.ETag.ToString());
        Assert.Null(await cache.GetAsync("employee:17"));
        Assert.Equal(otherCache, await cache.GetAsync("employee:18"));
        Assert.Equal(before, await SnapshotAsync(17));
        await using var db = fixture.CreateContext();
        Assert.Equal(email, (await db.Employees.AsNoTracking().SingleAsync(row => row.Id == 17)).Email);
        Assert.Equal(response.Headers.ETag.ToString(), await VersionAsync(email));
        using var repeated = await SendAsync(writer, true, email, response.Headers.ETag.ToString());
        Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
        Assert.Equal(email, (await db.Employees.AsNoTracking().SingleAsync(row => row.Id == 17)).Email);
        fixture.IamTransportEvidence.AssertHealthy();
    }

    /// <summary>Checks that padded email text of length 256 persists and length 257 fails without graph or cache changes.</summary>
    /// <param name="update">Whether to exercise ordinary employee update instead of employee creation.</param>
    /// <returns>A task that completes after asserting accepted capacity and sanitized oversized-write rollback with retained caches.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdministrativeEmail_Padded256PersistsAnd257RollsBackWithoutCacheEviction(bool update) =>
        await AssertPaddedCapacityAsync(update, false);

    /// <summary>Checks that an oversized conditional email write preserves the previously stored value, reusable version, and caches.</summary>
    /// <returns>A task that completes after asserting the 257-character refusal, sanitized response, unchanged graph, and retained valid ETag.</returns>
    [Fact]
    public async Task ConditionalAdministrativeEmail_Padded257RollsBackAndRetainsReusableVersion() =>
        await AssertPaddedCapacityAsync(true, true);

    /// <summary>Checks that unauthorized email creates and updates leave all rows and primed caches unchanged.</summary>
    /// <param name="update">Whether to exercise the employee update endpoint instead of creation.</param>
    /// <param name="authority">The authentication scenario selecting an anonymous actor or an actor without the required permission.</param>
    /// <param name="expected">The expected unauthorized or forbidden response for the selected actor.</param>
    /// <returns>A task that completes after asserting the refusal status and unchanged graph and cache bytes.</returns>
    [Theory]
    [InlineData(false, "anonymous", HttpStatusCode.Unauthorized)]
    [InlineData(true, "anonymous", HttpStatusCode.Unauthorized)]
    [InlineData(false, "missing-permission", HttpStatusCode.Forbidden)]
    [InlineData(true, "missing-permission", HttpStatusCode.Forbidden)]
    public async Task AdministrativeEmail_DeniedWritesPreserveWholeGraphAndCaches(bool update, string authority, HttpStatusCode expected)
    {
        await SeedAsync();
        using var scope = fixture.Factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var ownerCache = await PrimeAsync(cache, 17);
        var otherCache = await PrimeAsync(cache, 18);
        var before = await SnapshotAsync();
        using var writer = Writer(update, authority);
        using var response = await SendAsync(writer, update, "  private-synthetic@example.invalid  ");
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
        Assert.Equal(ownerCache, await cache.GetAsync("employee:17"));
        Assert.Equal(otherCache, await cache.GetAsync("employee:18"));
        fixture.IamTransportEvidence.AssertHealthy();
    }

    /// <summary>Checks that an email update using a stale version cannot alter the accepted literal email or graph caches.</summary>
    /// <returns>A task that completes after asserting precondition failure, retained graph and cache bytes, and the current accepted ETag.</returns>
    [Fact]
    public async Task ConditionalAdministrativeEmail_StaleVersionLeavesLiteralRowAndGraphCachesUntouched()
    {
        await SeedAsync();
        var originalVersion = await VersionAsync();
        const string savedEmail = "\tsaved@example.invalid\t";
        using var writer = Writer(true);
        using var saved = await SendAsync(writer, true, savedEmail, originalVersion);
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        using var scope = fixture.Factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var ownerCache = await PrimeAsync(cache, 17);
        var otherCache = await PrimeAsync(cache, 18);
        var before = await SnapshotAsync();
        using var rejected = await SendAsync(writer, true, "\tstale@example.invalid\t", originalVersion);
        Assert.Equal(HttpStatusCode.PreconditionFailed, rejected.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
        Assert.Equal(ownerCache, await cache.GetAsync("employee:17"));
        Assert.Equal(otherCache, await cache.GetAsync("employee:18"));
        Assert.Equal(saved.Headers.ETag!.ToString(), await VersionAsync(savedEmail));
        fixture.IamTransportEvidence.AssertHealthy();
    }

    private async Task AssertPaddedCapacityAsync(bool update, bool conditional)
    {
        await SeedAsync();
        using var writer = Writer(update);
        var version = conditional ? await VersionAsync() : null;
        var email = PaddedEmail(256);
        Assert.Equal(256, email.Length);
        using var accepted = await SendAsync(writer, update, email, version);
        Assert.Equal(update ? HttpStatusCode.NoContent : HttpStatusCode.Created, accepted.StatusCode);
        await using var db = fixture.CreateContext();
        var id = 17;
        if (!update)
        {
            using var created = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync());
            id = created.RootElement.GetProperty("Id").GetInt32();
        }
        Assert.Equal(email, (await db.Employees.AsNoTracking().SingleAsync(row => row.Id == id)).Email);
        var validVersion = conditional ? await VersionAsync(email) : null;
        using var scope = fixture.Factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var ownerCache = await PrimeAsync(cache, 17);
        var otherCache = await PrimeAsync(cache, 18);
        var before = await SnapshotAsync();
        var oversized = PaddedEmail(257);
        Assert.Equal(257, oversized.Length);
        using var rejected = await SendAsync(writer, update, oversized, validVersion);
        Assert.Equal(HttpStatusCode.InternalServerError, rejected.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
        Assert.Equal(ownerCache, await cache.GetAsync("employee:17"));
        Assert.Equal(otherCache, await cache.GetAsync("employee:18"));
        using var error = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        Assert.Equal(500, error.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal(JsonValueKind.Null, error.RootElement.GetProperty("details").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(error.RootElement.GetProperty("traceId").GetString()));
        var body = await rejected.Content.ReadAsStringAsync();
        foreach (var marker in new[] { oversized, "Npgsql", "character varying", "owner@example.invalid" })
            Assert.DoesNotContain(marker, body, StringComparison.OrdinalIgnoreCase);
        if (conditional) Assert.Equal(validVersion, await VersionAsync(email));
        fixture.IamTransportEvidence.AssertHealthy();
    }

    private HttpClient Writer(bool update, string authority = "valid") =>
        fixture.Client(update ? "legacy-employee.employees.update" : "legacy-employee.employees.create",
            update ? "/employees/17" : "global", authority: authority);

    private static string PaddedEmail(int length)
    {
        const string suffix = "@example.invalid";
        return "\t" + new string('e', length - suffix.Length - 2) + suffix + "\t";
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, bool update, string email, string? version = null)
    {
        using var request = new HttpRequestMessage(update ? HttpMethod.Put : HttpMethod.Post,
            update ? version is null ? "/employees/17/" : "/employees/17/versioned" : "/employees/")
        {
            Content = JsonContent.Create(new
            {
                Id = 999999,
                FirstName = "Owner",
                LastName = "Synthetic",
                PhoneNumber = "0900000000",
                Email = email,
                RoleId = 1,
                HomeAddressId = 1,
                DateOfBirth = new DateTime(1980, 1, 2),
                CreatedDate = new DateTime(1900, 1, 1),
                ModifiedDate = new DateTime(1900, 1, 1)
            })
        };
        if (version is not null) request.Headers.TryAddWithoutValidation("If-Match", version);
        return await client.SendAsync(request);
    }

    private async Task<string> VersionAsync(string? email = null)
    {
        using var reader = fixture.Client("legacy-employee.employees.read", "/employees/17");
        using var response = await reader.GetAsync("/employees/17/edit");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.ETag);
        if (email is not null)
        {
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(email, body.RootElement.GetProperty("Email").GetString());
        }
        return response.Headers.ETag.ToString();
    }

    private static async Task<byte[]> PrimeAsync(IDistributedCache cache, int id)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { id, email = $"cached-{id}@example.invalid" }));
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
            PhoneNumber = "0900000000",
            Email = "owner@example.invalid",
            RoleId = 1,
            HomeAddressId = 1,
            DateOfBirth = new DateTime(1980, 1, 2),
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
        return await db.Database.SqlQuery<string>($"""
            SELECT json_build_object(
                'Employees', (SELECT json_agg(json_build_array(row_to_json(t), t.xmin::text) ORDER BY t."ID") FROM "Employee" t WHERE t."ID" <> {excludedEmployeeId ?? -1}),
                'Addresses', (SELECT json_agg(json_build_array(row_to_json(t), t.xmin::text) ORDER BY t."ID") FROM "Address" t),
                'Roles', (SELECT json_agg(json_build_array(row_to_json(t), t.xmin::text) ORDER BY t."ID") FROM "Role" t),
                'Signatures', (SELECT json_agg(json_build_array(row_to_json(t), t.xmin::text) ORDER BY t."ID") FROM "SignatureImageFile" t)
            )::text AS "Value"
            """).SingleAsync();
    }
}

using System.Net;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.EmployeeService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.EmployeeService.Tests.AddressInput;

[Collection("Employee scaffold runtime")]
public sealed class EmployeeAddressInputHttpTests(EmployeeRouteAcceptanceFixture fixture)
    : IClassFixture<EmployeeRouteAcceptanceFixture>
{
    [Theory]
    [MemberData(nameof(EmployeeAddressInputCases.HttpCases), MemberType = typeof(EmployeeAddressInputCases))]
    public async Task HttpScalar_ValidatesBeforeWritesWithoutInputAuthority(string verb, string propertyName, string caseId)
    {
        var planned = EmployeeAddressInputCases.Cases(propertyName)[caseId];
        await AssertRequestAsync(verb, EmployeeAddressInputCases.Payload(propertyName, planned.Json), planned.Accepted);
    }

    [Theory]
    [MemberData(nameof(EmployeeAddressInputCases.HttpOrders), MemberType = typeof(EmployeeAddressInputCases))]
    public async Task HttpOrders_DoNotHideMalformedEarlierOccurrences(string verb, string propertyName, string orderId)
        => await AssertRequestAsync(verb, EmployeeAddressInputCases.Ordered(propertyName, orderId),
            EmployeeAddressInputCases.OrderAccepted(orderId));

    [Theory]
    [InlineData("post")]
    [InlineData("put")]
    public async Task ValidBodyIdentityAndAudit_HaveNoWriteAuthority(string verb)
    {
        var seed = await ResetAndSeedAsync();
        var body = "{\"Building\":\"literal ไทย\",\"CountryId\":764,\"Id\":" + seed.OtherAddressId
            + ",\"CreatedDate\":\"1901-02-03T04:05:06Z\",\"ModifiedDate\":\"1902-03-04T05:06:07Z\"}";
        await AssertRequestAsync(verb, body, true, seed);
    }

    [Theory]
    [InlineData("post", "anonymous", HttpStatusCode.Unauthorized)]
    [InlineData("put", "anonymous", HttpStatusCode.Unauthorized)]
    [InlineData("post", "missing-permission", HttpStatusCode.Forbidden)]
    [InlineData("put", "missing-permission", HttpStatusCode.Forbidden)]
    public async Task DeniedActor_HasNoAddressAuditOrCacheEffects(string verb, string authority, HttpStatusCode expected)
    {
        var seed = await ResetAndSeedAsync();
        var before = await SnapshotAsync();
        using var writer = Writer(verb, seed, authority);
        using var response = await SendAsync(writer, verb, seed,
            "{\"Building\":\"literal\",\"CountryId\":764,\"Id\":1,\"CreatedDate\":null}");
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
        await AssertCachesAsync(seed, false);
    }

    [Theory]
    [InlineData("missing", HttpStatusCode.PreconditionRequired)]
    [InlineData("malformed", HttpStatusCode.BadRequest)]
    [InlineData("stale", HttpStatusCode.PreconditionFailed)]
    [InlineData("current", HttpStatusCode.NoContent)]
    public async Task VersionedAddress_ValidInputKeepsPreconditionsAndServerAudits(string condition, HttpStatusCode expected)
    {
        var seed = await ResetAndSeedAsync();
        using var reader = fixture.Client("legacy-employee.addresses.read", $"/employees/addresses/{seed.AddressId}");
        using var read = await reader.GetAsync($"/employees/addresses/{seed.AddressId}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.NotNull(read.Headers.ETag);
        var version = read.Headers.ETag!.ToString();
        if (condition == "stale")
        {
            using var interimWriter = Writer("put", seed);
            using var changed = await SendAsync(interimWriter, "put", seed,
                "{\"Building\":\"intermediate\",\"CountryId\":764}");
            Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
            await SeedCachesAsync(seed);
        }
        var before = await SnapshotAsync();
        using var writer = Writer("put", seed);
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/employees/addresses/{seed.AddressId}/versioned")
        {
            Content = Json("{\"Building\":\"literal ไทย\",\"CountryId\":764,\"Id\":" + seed.OtherAddressId
                + ",\"CreatedDate\":\"1901-01-01\",\"ModifiedDate\":\"1902-01-01\"}"),
        };
        if (condition != "missing")
            request.Headers.TryAddWithoutValidation("If-Match", condition == "malformed" ? "not-a-version" : version);
        using var response = await writer.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
        if (condition == "current")
        {
            Assert.NotNull(response.Headers.ETag);
            Assert.NotEqual(version, response.Headers.ETag!.ToString());
            await using var db = fixture.CreateContext();
            var address = await db.Addresses.AsNoTracking().SingleAsync(a => a.Id == seed.AddressId);
            Assert.Equal("literal ไทย", address.Building);
            Assert.Equal(seed.CreatedDate, address.CreatedDate);
            Assert.NotEqual(new DateTime(1902, 1, 1), address.ModifiedDate);
            await AssertCachesAsync(seed, true);
        }
        else
        {
            Assert.Equal(before, await SnapshotAsync());
            await AssertCachesAsync(seed, false);
        }
    }

    [Theory]
    [InlineData("Id")]
    [InlineData("CreatedDate")]
    [InlineData("ModifiedDate")]
    public async Task VersionedAddress_MalformedInputDoesNotChangeEtagState(string propertyName)
    {
        var seed = await ResetAndSeedAsync();
        using var reader = fixture.Client("legacy-employee.addresses.read", $"/employees/addresses/{seed.AddressId}");
        using var read = await reader.GetAsync($"/employees/addresses/{seed.AddressId}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.NotNull(read.Headers.ETag);
        var version = read.Headers.ETag!.ToString();
        var before = await SnapshotAsync();
        using var writer = Writer("put", seed);
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/employees/addresses/{seed.AddressId}/versioned")
        {
            Content = Json(EmployeeAddressInputCases.Payload(propertyName, "{}")),
        };
        request.Headers.TryAddWithoutValidation("If-Match", version);
        using var response = await writer.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
        await AssertCachesAsync(seed, false);
        using var fresh = await reader.GetAsync($"/employees/addresses/{seed.AddressId}");
        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
        Assert.Equal(version, fresh.Headers.ETag!.ToString());
    }

    private async Task AssertRequestAsync(string verb, string json, bool accepted, Seed? supplied = null)
    {
        var seed = supplied ?? await ResetAndSeedAsync();
        var before = await SnapshotAsync();
        var untouched = await SnapshotAsync(verb == "put" ? seed.AddressId : null);
        var began = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        using var writer = Writer(verb, seed);
        using var response = await SendAsync(writer, verb, seed, json);
        var ended = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        if (!accepted)
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(before, await SnapshotAsync());
            await AssertCachesAsync(seed, false);
            return;
        }
        Assert.Equal(verb == "post" ? HttpStatusCode.Created : HttpStatusCode.NoContent, response.StatusCode);
        var id = seed.AddressId;
        if (verb == "post")
        {
            Assert.NotNull(response.Headers.Location);
            using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            id = created.RootElement.GetProperty("Id").GetInt32();
            Assert.NotEqual(seed.AddressId, id);
            Assert.NotEqual(seed.OtherAddressId, id);
            Assert.EndsWith($"/employees/addresses/{id}", response.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        await using var db = fixture.CreateContext();
        var address = await db.Addresses.AsNoTracking().SingleAsync(a => a.Id == id);
        Assert.Equal("literal ไทย", address.Building);
        Assert.Equal(764, address.CountryId);
        Assert.NotNull(address.ModifiedDate);
        Assert.InRange(address.ModifiedDate!.Value, began.AddSeconds(-1), ended.AddSeconds(1));
        if (verb == "post")
        {
            Assert.NotNull(address.CreatedDate);
            Assert.InRange(address.CreatedDate!.Value, began.AddSeconds(-1), ended.AddSeconds(1));
        }
        else Assert.Equal(seed.CreatedDate, address.CreatedDate);
        Assert.Equal(untouched, await SnapshotAsync(id));
        using var reader = fixture.Client("legacy-employee.addresses.read", $"/employees/addresses/{id}");
        using var fresh = await reader.GetAsync($"/employees/addresses/{id}");
        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
        using var wire = JsonDocument.Parse(await fresh.Content.ReadAsStringAsync());
        Assert.Equal(id, wire.RootElement.GetProperty("Id").GetInt32());
        Assert.Equal(address.CreatedDate, wire.RootElement.GetProperty("CreatedDate").GetDateTime());
        Assert.Equal(address.ModifiedDate, wire.RootElement.GetProperty("ModifiedDate").GetDateTime());
        await AssertCachesAsync(seed, verb == "put");
    }

    private async Task<Seed> ResetAndSeedAsync()
    {
        await using var db = fixture.CreateContext();
        using var scope = fixture.Factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        foreach (var id in await db.Employees.Select(employee => employee.Id).ToArrayAsync())
            await cache.RemoveAsync($"employee:{id}");
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"Employee\", \"Address\" CASCADE");
        fixture.Authorities.Clear();
        var created = new DateTime(2020, 1, 1);
        var firstAddress = new Address
        {
            Building = "seed",
            CountryId = 764,
            CreatedDate = created,
            ModifiedDate = created,
        };
        var otherAddress = new Address
        {
            Building = "other",
            CountryId = 764,
            CreatedDate = created,
            ModifiedDate = created,
        };
        db.Addresses.AddRange(firstAddress, otherAddress);
        await db.SaveChangesAsync();
        var first = new Employee
        {
            FirstName = "Synthetic",
            LastName = "First",
            HomeAddressId = firstAddress.Id,
        };
        var second = new Employee
        {
            FirstName = "Synthetic",
            LastName = "Second",
            HomeAddressId = firstAddress.Id,
        };
        var other = new Employee
        {
            FirstName = "Synthetic",
            LastName = "Other",
            HomeAddressId = otherAddress.Id,
        };
        db.Employees.AddRange(first, second, other);
        await db.SaveChangesAsync();
        var seed = new Seed(firstAddress.Id, otherAddress.Id, first.Id, second.Id, other.Id, created);
        await SeedCachesAsync(seed);
        return seed;
    }

    private HttpClient Writer(string verb, Seed seed, string authority = "valid")
        => fixture.Client(verb == "post" ? "legacy-employee.addresses.create" : "legacy-employee.addresses.update",
            verb == "post" ? "global" : $"/employees/addresses/{seed.AddressId}", authority);

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string verb, Seed seed, string json)
    {
        using var body = Json(json);
        return verb == "post" ? await client.PostAsync("/employees/addresses", body)
            : await client.PutAsync($"/employees/addresses/{seed.AddressId}", body);
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private async Task<string> SnapshotAsync(int? excludeAddress = null)
    {
        await using var db = fixture.CreateContext();
        return JsonSerializer.Serialize(new
        {
            Addresses = await db.Addresses.AsNoTracking().Where(address => excludeAddress == null || address.Id != excludeAddress)
                .OrderBy(address => address.Id).Select(address => new
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
                    address.ModifiedDate,
                }).ToListAsync(),
            Employees = await db.Employees.AsNoTracking().OrderBy(employee => employee.Id)
                .Select(employee => new { employee.Id, employee.HomeAddressId, employee.CreatedDate, employee.ModifiedDate }).ToListAsync(),
        });
    }

    private async Task SeedCachesAsync(Seed seed)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        foreach (var id in new[] { seed.EmployeeId, seed.SecondEmployeeId, seed.OtherEmployeeId })
            await cache.SetStringAsync($"employee:{id}", CacheValue(id));
    }

    private async Task AssertCachesAsync(Seed seed, bool changed)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        foreach (var id in new[] { seed.EmployeeId, seed.SecondEmployeeId })
        {
            var value = await cache.GetStringAsync($"employee:{id}");
            if (changed) Assert.Null(value);
            else Assert.Equal(CacheValue(id), value);
        }
        Assert.Equal(CacheValue(seed.OtherEmployeeId), await cache.GetStringAsync($"employee:{seed.OtherEmployeeId}"));
    }

    private static string CacheValue(int id) => "{\"Id\":" + id + ",\"Marker\":\"address-input-proof\"}";
    private sealed record Seed(int AddressId, int OtherAddressId, int EmployeeId, int SecondEmployeeId, int OtherEmployeeId, DateTime CreatedDate);
}

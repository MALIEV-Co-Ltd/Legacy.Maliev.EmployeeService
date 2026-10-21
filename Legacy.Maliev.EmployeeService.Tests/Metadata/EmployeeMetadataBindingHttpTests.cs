using System.Net;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.EmployeeService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.EmployeeService.Tests.Metadata;

// Existing disposable normal Production/JWT/PG18/Redis fixture; never cloud storage or Role writes.
/// <summary>Verifies consumed address and signature text binding through authenticated HTTP without changing unrelated persisted state.</summary>
/// <param name="fixture">The shared Employee route fixture supplying the production application factory, PostgreSQL contexts, Redis, and controlled IAM request authority.</param>
[Collection("Employee scaffold runtime")]
public sealed class EmployeeMetadataBindingHttpTests(EmployeeRouteAcceptanceFixture fixture)
    : IClassFixture<EmployeeRouteAcceptanceFixture>
{
    /// <summary>Checks that later duplicate nulls retain consumed metadata text and leave unrelated state unchanged.</summary>
    /// <param name="field">The consumed Building, AddressLine1, AddressLine2, City, State, PostalCode, Bucket, or ObjectName property to exercise.</param>
    /// <param name="verb">The post address-create or put metadata-update operation selected by the case.</param>
    /// <returns>A task that completes after verifying that later duplicate nulls retain consumed metadata text and leave unrelated state unchanged.</returns>
    [Theory]
    [MemberData(nameof(EmployeeMetadataBindingCases.HttpFieldCases), MemberType = typeof(EmployeeMetadataBindingCases))]
    public async Task HttpOrderedNull_KeepsConsumedTextAndUnrelatedState(string field, string verb)
        => await AssertRoundTripAsync(field, verb, ["\"first\"", "null"]);

    /// <summary>Checks preservation of the public-reader decimal lexeme through metadata HTTP writes.</summary>
    /// <param name="field">The consumed Building, AddressLine1, AddressLine2, City, State, PostalCode, Bucket, or ObjectName property to exercise.</param>
    /// <param name="verb">The post address-create or put metadata-update operation selected by the case.</param>
    /// <returns>A task that completes after verifying preservation of the public-reader decimal lexeme through metadata HTTP writes.</returns>
    [Theory]
    [InlineData("Building", "post")]
    [InlineData("AddressLine1", "put")]
    [InlineData("AddressLine2", "put")]
    [InlineData("City", "put")]
    [InlineData("State", "put")]
    [InlineData("PostalCode", "put")]
    [InlineData("Bucket", "put")]
    [InlineData("ObjectName", "put")]
    public async Task HttpPrimitive_KeepsPublicReaderLexeme(string field, string verb)
        => await AssertRoundTripAsync(field, verb, ["12.50"]);

    /// <summary>Checks that earlier malformed metadata text returns 400 despite later valid text without changing data or caches.</summary>
    /// <param name="field">The consumed Building, AddressLine1, AddressLine2, City, State, PostalCode, Bucket, or ObjectName property to exercise.</param>
    /// <param name="token">The raw object or array JSON token inserted before a valid duplicate text value.</param>
    /// <returns>A task that completes after verifying that earlier malformed metadata text returns 400 despite later valid text without changing data or caches.</returns>
    [Theory]
    [MemberData(nameof(EmployeeMetadataBindingCases.HttpMalformedCases), MemberType = typeof(EmployeeMetadataBindingCases))]
    public async Task HttpMalformedOccurrenceBeforeValid_Is400WithoutMutation(string field, string token)
    {
        var seed = await ResetAndSeedAsync();
        var json = WithEmployee(EmployeeMetadataBindingCases.Payload(field, token, "\"valid\""), seed.EmployeeId);
        var before = await SnapshotAsync();
        using var writer = Writer(field, "put", seed);
        using var response = await SendAsync(writer, field, "put", seed, json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
        await AssertSeedCacheAsync(seed);
    }

    /// <summary>Checks that denied metadata actors cannot mutate address or signature data or evict caches.</summary>
    /// <param name="field">The Building or Bucket property selecting the address or signature metadata route.</param>
    /// <param name="authority">The anonymous or missing-permission authority scenario used to verify rejection before mutation.</param>
    /// <param name="expected">The expected Unauthorized or Forbidden status for the selected authority.</param>
    /// <returns>A task that completes after verifying that denied metadata actors cannot mutate address or signature data or evict caches.</returns>
    [Theory]
    [InlineData("Building", "anonymous", HttpStatusCode.Unauthorized)]
    [InlineData("Building", "missing-permission", HttpStatusCode.Forbidden)]
    [InlineData("Bucket", "anonymous", HttpStatusCode.Unauthorized)]
    [InlineData("Bucket", "missing-permission", HttpStatusCode.Forbidden)]
    public async Task HttpDeniedActor_HasNoMetadataEffects(string field, string authority, HttpStatusCode expected)
    {
        var seed = await ResetAndSeedAsync();
        var before = await SnapshotAsync();
        using var writer = Writer(field, "put", seed, authority);
        using var response = await SendAsync(writer, field, "put", seed,
            WithEmployee(EmployeeMetadataBindingCases.Payload(field, "\"first\"", "null"), seed.EmployeeId));
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
        await AssertSeedCacheAsync(seed);
    }

    /// <summary>Checks conditional address statuses, duplicate-null text retention, and cache effects.</summary>
    /// <param name="condition">The If-Match scenario: missing, malformed, stale, or current.</param>
    /// <param name="expected">The expected 428, 400, 412, or 204 status for the selected If-Match scenario.</param>
    /// <returns>A task that completes after verifying conditional address statuses, duplicate-null text retention, and cache effects.</returns>
    [Theory]
    [InlineData("missing", HttpStatusCode.PreconditionRequired)]
    [InlineData("malformed", HttpStatusCode.BadRequest)]
    [InlineData("stale", HttpStatusCode.PreconditionFailed)]
    [InlineData("current", HttpStatusCode.NoContent)]
    public async Task VersionedAddress_KeepsPreconditionsCacheAndTextBinding(string condition, HttpStatusCode expected)
    {
        var seed = await ResetAndSeedAsync();
        using var reader = fixture.Client("legacy-employee.addresses.read", $"/employees/addresses/{seed.AddressId}");
        using var read = await reader.GetAsync($"/employees/addresses/{seed.AddressId}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.NotNull(read.Headers.ETag);
        var version = read.Headers.ETag!.ToString();
        if (condition == "stale")
        {
            using var interimWriter = Writer("Building", "put", seed);
            using var changed = await SendAsync(interimWriter, "Building", "put", seed,
                "{\"Building\":\"intermediate\",\"CountryId\":764}");
            Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
            await SeedCacheAsync(seed);
        }
        var before = await SnapshotAsync();
        using var writer = Writer("Building", "put", seed);
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/employees/addresses/{seed.AddressId}/versioned")
        {
            Content = Json(EmployeeMetadataBindingCases.Payload("Building", "\"first\"", "null")),
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
            Assert.Equal("first", (await db.Addresses.AsNoTracking().SingleAsync(x => x.Id == seed.AddressId)).Building);
            await AssertCacheAbsentAsync(seed);
        }
        else
        {
            Assert.Equal(before, await SnapshotAsync());
            await AssertSeedCacheAsync(seed);
        }
    }

    private async Task AssertRoundTripAsync(string field, string verb, string[] tokens)
    {
        var seed = await ResetAndSeedAsync();
        var address = EmployeeMetadataBindingCases.IsAddress(field);
        var json = WithEmployee(EmployeeMetadataBindingCases.Payload(field, tokens), seed.EmployeeId);
        var expected = EmployeeMetadataBindingCases.Field(EmployeeMetadataBindingCases.PublicProjection(json), field);
        var untouched = await SnapshotAsync(address && verb == "put" ? seed.AddressId : null,
            !address ? seed.SignatureId : null);
        using var writer = Writer(field, verb, seed);
        using var response = await SendAsync(writer, field, verb, seed, json);
        Assert.Equal(verb == "post" ? HttpStatusCode.Created : HttpStatusCode.NoContent, response.StatusCode);
        var id = seed.AddressId;
        if (verb == "post")
        {
            Assert.NotNull(response.Headers.Location);
            using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            id = created.RootElement.GetProperty("Id").GetInt32();
            Assert.EndsWith($"/employees/addresses/{id}", response.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        var readPath = address ? $"/employees/addresses/{id}" : $"/employees/signatures/{seed.EmployeeId}";
        using var reader = fixture.Client(address ? "legacy-employee.addresses.read" : "legacy-employee.signatures.read",
            address ? $"/employees/addresses/{id}" : $"/employees/{seed.EmployeeId}/signature");
        using var fresh = await reader.GetAsync(readPath);
        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
        using var wire = JsonDocument.Parse(await fresh.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.String, wire.RootElement.GetProperty(field).ValueKind);
        Assert.Equal(expected, wire.RootElement.GetProperty(field).GetString());
        await using var db = fixture.CreateContext();
        object stored = address
            ? await db.Addresses.AsNoTracking().SingleAsync(x => x.Id == id)
            : await db.SignatureImageFiles.AsNoTracking().SingleAsync(x => x.Id == seed.SignatureId);
        Assert.Equal(expected, EmployeeMetadataBindingCases.Field(stored, field));
        Assert.Equal(untouched, await SnapshotAsync(address ? id : null, !address ? seed.SignatureId : null));
        if (address && verb == "put") await AssertCacheAbsentAsync(seed);
        else await AssertSeedCacheAsync(seed);
        if (!address)
        {
            var signature = Assert.IsType<SignatureImageFile>(stored);
            Assert.Equal(seed.EmployeeId, signature.EmployeeId);
            Assert.Equal(seed.CreatedDate, signature.CreatedDate);
        }
    }

    private async Task<Seed> ResetAndSeedAsync()
    {
        await using var db = fixture.CreateContext();
        using var scope = fixture.Factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        foreach (var id in await db.Employees.Select(x => x.Id).ToArrayAsync())
            await cache.RemoveAsync($"employee:{id}");
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"Employee\", \"Address\", \"SignatureImageFile\" CASCADE");
        fixture.Authorities.Clear();
        var address = new Address { Building = "seed", AddressLine1 = "seed road", CountryId = 764 };
        db.Addresses.Add(address);
        await db.SaveChangesAsync();
        var first = new Employee
        {
            FirstName = "Synthetic",
            LastName = "First",
            Email = "first@example.invalid",
            HomeAddressId = address.Id,
        };
        var second = new Employee
        {
            FirstName = "Synthetic",
            LastName = "Second",
            Email = "second@example.invalid",
            HomeAddressId = address.Id,
        };
        db.Employees.AddRange(first, second);
        await db.SaveChangesAsync();
        var created = new DateTime(2020, 1, 1);
        var signature = new SignatureImageFile
        {
            EmployeeId = first.Id,
            Bucket = "seed",
            ObjectName = "seed.png",
            CreatedDate = created,
            ModifiedDate = created,
        };
        db.SignatureImageFiles.Add(signature);
        await db.SaveChangesAsync();
        var seed = new Seed(address.Id, first.Id, second.Id, signature.Id, created);
        await SeedCacheAsync(seed);
        return seed;
    }

    private HttpClient Writer(string field, string verb, Seed seed, string authority = "valid")
    {
        var address = EmployeeMetadataBindingCases.IsAddress(field);
        var permission = address
            ? verb == "post" ? "legacy-employee.addresses.create" : "legacy-employee.addresses.update"
            : "legacy-employee.signatures.write";
        var resource = address ? verb == "post" ? "global" : $"/employees/addresses/{seed.AddressId}"
            : $"/employees/{seed.EmployeeId}/signature";
        return fixture.Client(permission, resource, authority);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string field, string verb, Seed seed, string json)
    {
        using var content = Json(json);
        var address = EmployeeMetadataBindingCases.IsAddress(field);
        var path = address ? verb == "post" ? "/employees/addresses" : $"/employees/addresses/{seed.AddressId}"
            : $"/employees/signatures/{seed.EmployeeId}";
        return verb == "post" ? await client.PostAsync(path, content) : await client.PutAsync(path, content);
    }

    private async Task SeedCacheAsync(Seed seed)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        foreach (var id in new[] { seed.EmployeeId, seed.OtherEmployeeId })
            await cache.SetStringAsync($"employee:{id}", "SYNTHETIC-UNCHANGED-CACHE");
    }
    private async Task AssertSeedCacheAsync(Seed seed)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        foreach (var id in new[] { seed.EmployeeId, seed.OtherEmployeeId })
            Assert.Equal("SYNTHETIC-UNCHANGED-CACHE", await cache.GetStringAsync($"employee:{id}"));
    }
    private async Task AssertCacheAbsentAsync(Seed seed)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        foreach (var id in new[] { seed.EmployeeId, seed.OtherEmployeeId })
            Assert.Null(await cache.GetStringAsync($"employee:{id}"));
    }

    private async Task<string> SnapshotAsync(int? exceptAddress = null, int? exceptSignature = null)
    {
        await using var db = fixture.CreateContext();
        var rows = new List<string>();
        foreach (var table in new[] { "Employee", "Address", "SignatureImageFile" })
        {
            var id = table == "Address" ? exceptAddress : table == "SignatureImageFile" ? exceptSignature : null;
            var query = (table, id.HasValue) switch
            {
                ("Employee", false) => "SELECT COALESCE(jsonb_agg(to_jsonb(r) ORDER BY to_jsonb(r)::text)::text,'[]') AS \"Value\" FROM (SELECT * FROM public.\"Employee\") r",
                ("Address", false) => "SELECT COALESCE(jsonb_agg(to_jsonb(r) ORDER BY to_jsonb(r)::text)::text,'[]') AS \"Value\" FROM (SELECT * FROM public.\"Address\") r",
                ("Address", true) => "SELECT COALESCE(jsonb_agg(to_jsonb(r) ORDER BY to_jsonb(r)::text)::text,'[]') AS \"Value\" FROM (SELECT * FROM public.\"Address\" WHERE \"ID\" <> {0}) r",
                ("SignatureImageFile", false) => "SELECT COALESCE(jsonb_agg(to_jsonb(r) ORDER BY to_jsonb(r)::text)::text,'[]') AS \"Value\" FROM (SELECT * FROM public.\"SignatureImageFile\") r",
                ("SignatureImageFile", true) => "SELECT COALESCE(jsonb_agg(to_jsonb(r) ORDER BY to_jsonb(r)::text)::text,'[]') AS \"Value\" FROM (SELECT * FROM public.\"SignatureImageFile\" WHERE \"ID\" <> {0}) r",
                _ => throw new InvalidOperationException("Unexpected snapshot table/filter combination."),
            };
            rows.Add(table + ":" + await (id.HasValue
                ? db.Database.SqlQueryRaw<string>(query, id.Value)
                : db.Database.SqlQueryRaw<string>(query)).SingleAsync());
        }
        return string.Join("\n", rows);
    }
    private static string WithEmployee(string json, int employeeId)
        => json.Replace("\"EmployeeId\":1", $"\"EmployeeId\":{employeeId}", StringComparison.Ordinal);
    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
    private sealed record Seed(int AddressId, int EmployeeId, int OtherEmployeeId, int SignatureId, DateTime CreatedDate);
}

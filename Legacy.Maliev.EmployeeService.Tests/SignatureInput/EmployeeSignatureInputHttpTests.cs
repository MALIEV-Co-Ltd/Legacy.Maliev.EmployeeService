using System.Net;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.EmployeeService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.EmployeeService.Tests.SignatureInput;

[Collection("Employee scaffold runtime")]
public sealed class EmployeeSignatureInputHttpTests(EmployeeRouteAcceptanceFixture fixture)
    : IClassFixture<EmployeeRouteAcceptanceFixture>
{
    [Theory]
    [MemberData(nameof(EmployeeSignatureInputCases.HttpCases), MemberType = typeof(EmployeeSignatureInputCases))]
    public async Task HttpScalar_ValidatesBeforeWritesWithoutInputAuthority(string propertyName, string caseId)
    {
        var planned = EmployeeSignatureInputCases.Cases(propertyName)[caseId];
        await AssertRequestAsync(EmployeeSignatureInputCases.Payload(propertyName, planned.Json), planned.Accepted);
    }

    [Theory]
    [MemberData(nameof(EmployeeSignatureInputCases.HttpOrders), MemberType = typeof(EmployeeSignatureInputCases))]
    public async Task HttpOrders_DoNotHideMalformedEarlierOccurrences(string propertyName, string orderId)
        => await AssertRequestAsync(EmployeeSignatureInputCases.Ordered(propertyName, orderId),
            EmployeeSignatureInputCases.OrderAccepted(orderId));

    [Fact]
    public async Task ValidBodyIdentityAndAudit_HaveNoWriteAuthority()
    {
        var seed = await ResetAndSeedAsync();
        await AssertRequestAsync("{\"Bucket\":\"literal ไทย\",\"ObjectName\":\"literal ไทย.png\",\"Id\":"
            + seed.OtherSignatureId + ",\"CreatedDate\":\"1901-02-03T04:05:06Z\",\"ModifiedDate\":\"1902-03-04T05:06:07Z\"}",
            true, seed);
    }

    [Theory]
    [InlineData("anonymous", HttpStatusCode.Unauthorized)]
    [InlineData("missing-permission", HttpStatusCode.Forbidden)]
    public async Task DeniedActor_HasNoSignatureAuditOrCacheEffects(string authority, HttpStatusCode expected)
    {
        var seed = await ResetAndSeedAsync();
        var before = await SnapshotAsync();
        using var writer = Writer(seed, authority);
        using var body = Json(EmployeeSignatureInputCases.Payload("Id", "42"));
        using var response = await writer.PutAsync($"/employees/signatures/{seed.EmployeeId}", body);
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
        await AssertCachesAsync(seed, false);
    }

    [Fact]
    public async Task PostQuery_DoesNotAcquireJsonBodyValidation()
    {
        var seed = await ResetAndSeedAsync();
        await using (var db = fixture.CreateContext())
            await db.SignatureImageFiles.Where(row => row.Id == seed.SignatureId).ExecuteDeleteAsync();
        using var writer = fixture.Client("legacy-employee.signatures.write", $"/employees/{seed.EmployeeId}");
        using var body = Json("{\"Id\":{},\"CreatedDate\":[],\"ModifiedDate\":true}");
        using var response = await writer.PostAsync(
            $"/employees/{seed.EmployeeId}/signatures?bucket=query-literal&objectName=query-object.png", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var fresh = fixture.CreateContext();
        var saved = await fresh.SignatureImageFiles.AsNoTracking().SingleAsync(row => row.EmployeeId == seed.EmployeeId);
        Assert.Equal("query-literal", saved.Bucket);
        Assert.Equal("query-object.png", saved.ObjectName);
        Assert.NotEqual(seed.CreatedDate, saved.CreatedDate);
        Assert.Equal(seed.OtherSignatureId, (await fresh.SignatureImageFiles.AsNoTracking()
            .SingleAsync(row => row.EmployeeId == seed.OtherEmployeeId)).Id);
    }

    private async Task AssertRequestAsync(string json, bool accepted, Seed? supplied = null)
    {
        var seed = supplied ?? await ResetAndSeedAsync();
        json = json.Replace("\"EmployeeId\":7", "\"EmployeeId\":" + seed.EmployeeId, StringComparison.Ordinal);
        var before = await SnapshotAsync();
        var untouched = await SnapshotAsync(seed.SignatureId);
        var began = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        using var writer = Writer(seed);
        using var body = Json(json);
        using var response = await writer.PutAsync($"/employees/signatures/{seed.EmployeeId}", body);
        var ended = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        if (!accepted)
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(before, await SnapshotAsync());
            await AssertCachesAsync(seed, false);
            return;
        }
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var db = fixture.CreateContext();
        var signature = await db.SignatureImageFiles.AsNoTracking().SingleAsync(row => row.Id == seed.SignatureId);
        Assert.Equal(seed.EmployeeId, signature.EmployeeId);
        Assert.Equal("literal ไทย", signature.Bucket);
        Assert.Equal("literal ไทย.png", signature.ObjectName);
        Assert.Equal(seed.CreatedDate, signature.CreatedDate);
        Assert.NotNull(signature.ModifiedDate);
        Assert.InRange(signature.ModifiedDate!.Value, began.AddSeconds(-1), ended.AddSeconds(1));
        Assert.Equal(untouched, await SnapshotAsync(seed.SignatureId));
        using var reader = fixture.Client("legacy-employee.signatures.read", $"/employees/{seed.EmployeeId}/signature");
        using var read = await reader.GetAsync($"/employees/signatures/{seed.EmployeeId}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var wire = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.Equal(seed.SignatureId, wire.RootElement.GetProperty("Id").GetInt32());
        Assert.Equal(seed.EmployeeId, wire.RootElement.GetProperty("EmployeeId").GetInt32());
        Assert.Equal(signature.CreatedDate, wire.RootElement.GetProperty("CreatedDate").GetDateTime());
        Assert.Equal(signature.ModifiedDate, wire.RootElement.GetProperty("ModifiedDate").GetDateTime());
        await AssertCachesAsync(seed, false);
    }

    private async Task<Seed> ResetAndSeedAsync()
    {
        await using var db = fixture.CreateContext();
        using var scope = fixture.Factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        foreach (var id in await db.Employees.Select(employee => employee.Id).ToArrayAsync())
            await cache.RemoveAsync($"employee:{id}");
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"SignatureImageFile\", \"Employee\" CASCADE");
        fixture.Authorities.Clear();
        var first = new Employee
        {
            FirstName = "Synthetic",
            LastName = "SignatureFirst",
        };
        var other = new Employee
        {
            FirstName = "Synthetic",
            LastName = "SignatureOther",
        };
        db.Employees.AddRange(first, other);
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
        var otherSignature = new SignatureImageFile
        {
            EmployeeId = other.Id,
            Bucket = "other",
            ObjectName = "other.png",
            CreatedDate = created,
            ModifiedDate = created,
        };
        db.SignatureImageFiles.AddRange(signature, otherSignature);
        await db.SaveChangesAsync();
        var seed = new Seed(signature.Id, otherSignature.Id, first.Id, other.Id, created);
        foreach (var id in new[] { seed.EmployeeId, seed.OtherEmployeeId })
            await cache.SetStringAsync($"employee:{id}", CacheValue(id));
        return seed;
    }

    private HttpClient Writer(Seed seed, string authority = "valid")
        => fixture.Client("legacy-employee.signatures.write", $"/employees/{seed.EmployeeId}/signature", authority);

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private async Task<string> SnapshotAsync(int? excludeSignature = null)
    {
        await using var db = fixture.CreateContext();
        return JsonSerializer.Serialize(new
        {
            Signatures = await db.SignatureImageFiles.AsNoTracking()
                .Where(row => excludeSignature == null || row.Id != excludeSignature).OrderBy(row => row.Id)
                .Select(row => new { row.Id, row.EmployeeId, row.Bucket, row.ObjectName, row.CreatedDate, row.ModifiedDate }).ToListAsync(),
            Employees = await db.Employees.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new { row.Id, row.HomeAddressId, row.CreatedDate, row.ModifiedDate }).ToListAsync(),
            Addresses = await db.Addresses.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new { row.Id, row.Building, row.CountryId, row.CreatedDate, row.ModifiedDate }).ToListAsync(),
        });
    }

    private async Task AssertCachesAsync(Seed seed, bool changed)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var value = await cache.GetStringAsync($"employee:{seed.EmployeeId}");
        if (changed) Assert.Null(value);
        else Assert.Equal(CacheValue(seed.EmployeeId), value);
        Assert.Equal(CacheValue(seed.OtherEmployeeId), await cache.GetStringAsync($"employee:{seed.OtherEmployeeId}"));
    }

    private static string CacheValue(int id) => "{\"Id\":" + id + ",\"Marker\":\"signature-input-proof\"}";
    private sealed record Seed(int SignatureId, int OtherSignatureId, int EmployeeId, int OtherEmployeeId, DateTime CreatedDate);
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.EmployeeService.Domain;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.EmployeeService.Tests;

[CollectionDefinition("Employee signature persistence", DisableParallelization = true)]
public sealed class EmployeeSignaturePersistenceCollection;

[Collection("Employee signature persistence")]
public sealed class EmployeeSignaturePersistenceHttpTests(EmployeeRouteAcceptanceFixture fixture)
    : IClassFixture<EmployeeRouteAcceptanceFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BucketLimit_AcceptsFiftyCharactersAndRollsBackFiftyOneWithoutLeakingMetadata(bool update)
    {
        await SeedAsync(update);
        var bucket = new string('ก', 50);
        using var client = Writer(update);
        using var accepted = await SendAsync(client, update, bucket, "accepted.png");
        Assert.Equal(update ? HttpStatusCode.NoContent : HttpStatusCode.Created, accepted.StatusCode);
        var before = await SnapshotAsync();
        using var rejected = await SendAsync(client, update, bucket + "ก", "private-rejected-object.png");
        Assert.Equal(HttpStatusCode.InternalServerError, rejected.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
        using var error = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        Assert.Equal(500, error.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal(JsonValueKind.Null, error.RootElement.GetProperty("details").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(error.RootElement.GetProperty("traceId").GetString()));
        var body = await rejected.Content.ReadAsStringAsync();
        foreach (var marker in new[] { bucket, "private-rejected-object", "Npgsql", "SignatureImageFile" })
            Assert.DoesNotContain(marker, body, StringComparison.OrdinalIgnoreCase);
        fixture.IamTransportEvidence.AssertHealthy();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LongObjectName_QueryAndBodyRoundTripWithoutInventedStorageLimitOrCloudIo(bool update)
    {
        await SeedAsync(update);
        var objectName = "signatures/" + new string('x', 4096) + "+%?.png";
        using var client = Writer(update);
        using var response = await SendAsync(client, update, "synthetic-private", objectName);
        Assert.Equal(update ? HttpStatusCode.NoContent : HttpStatusCode.Created, response.StatusCode);
        await using var db = fixture.CreateContext();
        var saved = await db.SignatureImageFiles.AsNoTracking().SingleAsync(row => row.EmployeeId == 17);
        Assert.Equal(objectName, saved.ObjectName);
        using var reader = fixture.Client("legacy-employee.signatures.read", "/employees/17/signature");
        using var read = await reader.GetAsync("/employees/signatures/17/");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var json = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.Equal(saved.Id, json.RootElement.GetProperty("Id").GetInt32());
        Assert.Equal(objectName, json.RootElement.GetProperty("ObjectName").GetString());
        Assert.False(json.RootElement.TryGetProperty("objectName", out _));
        await AssertUnrelatedAsync(db);
        fixture.IamTransportEvidence.AssertHealthy();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(int.MaxValue)]
    public async Task SignatureReassignment_PreservesSourceScalarEmployeeIdWithoutInventedForeignKey(int employeeId)
    {
        await SeedAsync(true);
        using var writer = Writer(true);
        using var updated = await writer.PutAsJsonAsync("/employees/signatures/17", new
        {
            Id = 999999, EmployeeId = employeeId, Bucket = "replacement", ObjectName = "replacement.png",
            CreatedDate = new DateTime(1900, 1, 1)
        });
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
        await using var db = fixture.CreateContext();
        Assert.False(await db.Employees.AnyAsync(row => row.Id == employeeId));
        var saved = await db.SignatureImageFiles.AsNoTracking().SingleAsync(row => row.EmployeeId == employeeId);
        Assert.Equal(101, saved.Id);
        Assert.Equal(new DateTime(2020, 1, 1), saved.CreatedDate);
        Assert.True(saved.ModifiedDate > new DateTime(2020, 1, 2));
        using var oldReader = fixture.Client("legacy-employee.signatures.read", "/employees/17/signature");
        using var oldRead = await oldReader.GetAsync("/employees/signatures/17");
        Assert.Equal(HttpStatusCode.NotFound, oldRead.StatusCode);
        using var newReader = fixture.Client("legacy-employee.signatures.read", $"/employees/{employeeId}/signature");
        using var newRead = await newReader.GetAsync($"/employees/signatures/{employeeId}/");
        Assert.Equal(HttpStatusCode.OK, newRead.StatusCode);
        using var json = JsonDocument.Parse(await newRead.Content.ReadAsStringAsync());
        Assert.Equal(employeeId, json.RootElement.GetProperty("EmployeeId").GetInt32());
        Assert.Equal("replacement.png", json.RootElement.GetProperty("ObjectName").GetString());
        await AssertUnrelatedAsync(db);
        fixture.IamTransportEvidence.AssertHealthy();
    }

    private HttpClient Writer(bool update) => fixture.Client("legacy-employee.signatures.write",
        update ? "/employees/17/signature" : "/employees/17");

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, bool update, string bucket, string objectName) => update
        ? client.PutAsJsonAsync("/employees/signatures/17", new { Bucket = bucket, ObjectName = objectName })
        : client.PostAsync($"/employees/17/signatures?bucket={Uri.EscapeDataString(bucket)}&objectName={Uri.EscapeDataString(objectName)}", null);

    private async Task SeedAsync(bool signature)
    {
        await using var db = fixture.CreateContext();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"Employee\", \"Address\", \"Role\", \"SignatureImageFile\" RESTART IDENTITY CASCADE");
        db.Employees.AddRange(
            new Employee { Id = 17, FirstName = "Owner", LastName = "Synthetic", Email = "owner@example.invalid" },
            new Employee { Id = 18, FirstName = "Other", LastName = "Synthetic", Email = "other@example.invalid" });
        if (signature) db.SignatureImageFiles.Add(new SignatureImageFile
        {
            Id = 101, EmployeeId = 17, Bucket = "original", ObjectName = "original.png",
            CreatedDate = new DateTime(2020, 1, 1), ModifiedDate = new DateTime(2020, 1, 2)
        });
        db.SignatureImageFiles.Add(new SignatureImageFile
        {
            Id = 301, EmployeeId = 18, Bucket = "unrelated", ObjectName = "unrelated.png",
            CreatedDate = new DateTime(2020, 1, 1), ModifiedDate = new DateTime(2020, 1, 2)
        });
        await db.SaveChangesAsync();
        fixture.Authorities.Clear();
    }

    private static async Task AssertUnrelatedAsync(Legacy.Maliev.EmployeeService.Data.EmployeeDbContext db)
    {
        Assert.Equal(2, await db.Employees.CountAsync());
        var unrelated = await db.SignatureImageFiles.AsNoTracking().SingleAsync(row => row.EmployeeId == 18);
        Assert.Equal(301, unrelated.Id);
        Assert.Equal("unrelated", unrelated.Bucket);
        Assert.Equal("unrelated.png", unrelated.ObjectName);
        Assert.Equal(new DateTime(2020, 1, 2), unrelated.ModifiedDate);
    }

    private async Task<string> SnapshotAsync()
    {
        await using var db = fixture.CreateContext();
        return JsonSerializer.Serialize(new
        {
            Employees = await db.Employees.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Signatures = await db.SignatureImageFiles.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync()
        });
    }
}

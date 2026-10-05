using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.EmployeeService.Domain;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.EmployeeService.Tests;

[CollectionDefinition("Employee signature ambiguity", DisableParallelization = true)]
public sealed class EmployeeSignatureAmbiguityCollection;

[Collection("Employee signature ambiguity")]
public sealed class EmployeeSignatureAmbiguityHttpTests(EmployeeRouteAcceptanceFixture fixture)
    : IClassFixture<EmployeeRouteAcceptanceFixture>
{
    private const int OwnerId = 17;
    private const int OtherId = 18;
    private const string Route = "/employees/signatures/17";
    private const string Resource = "/employees/17/signature";

    [Theory]
    [InlineData(false, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    public async Task DuplicateEmployeeMetadata_PreservesSourceServerFailureAndEveryStoredField(bool update, int matches)
    {
        await SeedAsync(matches);
        var before = await SnapshotAsync();
        using var client = fixture.Client(Permission(update), Resource);
        using var response = await SendAsync(client, update);
        fixture.IamTransportEvidence.AssertHealthy();
        Assert.Equal(before, await SnapshotAsync());
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var error = JsonDocument.Parse(body);
        Assert.Equal(500, error.RootElement.GetProperty("status").GetInt32());
        foreach (var privateValue in new[] { "signature-private-marker", "owner-private-marker", "Npgsql", "SingleOrDefault", "Sequence", "SignatureImageFile" })
        {
            Assert.DoesNotContain(privateValue, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UniqueEmployeeMetadata_PreservesReadAndUpdateContract(bool update)
    {
        await SeedAsync(1);
        var before = await SnapshotAsync();
        using var client = fixture.Client(Permission(update), Resource);
        using var response = await SendAsync(client, update);
        fixture.IamTransportEvidence.AssertHealthy();
        Assert.Equal(update ? HttpStatusCode.NoContent : HttpStatusCode.OK, response.StatusCode);
        await using var db = fixture.CreateContext();
        var owner = await db.SignatureImageFiles.SingleAsync(row => row.EmployeeId == OwnerId);
        var other = await db.SignatureImageFiles.SingleAsync(row => row.EmployeeId == OtherId);
        Assert.Equal("other-private-marker.png", other.ObjectName);
        Assert.Equal("signature-private-marker", other.Bucket);
        Assert.Equal(new DateTime(2020, 1, 1), owner.CreatedDate);
        Assert.Equal(OwnerId, owner.EmployeeId);
        Assert.Equal(101, owner.Id);
        if (update)
        {
            Assert.Equal("replacement-bucket", owner.Bucket);
            Assert.Equal("replacement.png", owner.ObjectName);
            Assert.True(owner.ModifiedDate > new DateTime(2020, 1, 2));
            Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
        }
        else
        {
            Assert.Equal(before, await SnapshotAsync());
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(OwnerId, json.RootElement.GetProperty("EmployeeId").GetInt32());
            Assert.Equal("owner-private-marker-0.png", json.RootElement.GetProperty("ObjectName").GetString());
            Assert.False(json.RootElement.TryGetProperty("employeeId", out _));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingEmployeeMetadata_RemainsNotFoundWithoutMutation(bool update)
    {
        await SeedAsync(0);
        var before = await SnapshotAsync();
        using var client = fixture.Client(Permission(update), Resource);
        using var response = await SendAsync(client, update);
        fixture.IamTransportEvidence.AssertHealthy();
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Theory]
    [InlineData(false, "anonymous", HttpStatusCode.Unauthorized)]
    [InlineData(false, "wrong-signature", HttpStatusCode.Unauthorized)]
    [InlineData(false, "missing-permission", HttpStatusCode.Forbidden)]
    [InlineData(true, "anonymous", HttpStatusCode.Unauthorized)]
    [InlineData(true, "wrong-signature", HttpStatusCode.Unauthorized)]
    [InlineData(true, "missing-permission", HttpStatusCode.Forbidden)]
    public async Task InvalidAuthority_RefusesBeforeDuplicateFailureAndPreservesMetadata(bool update, string authority, HttpStatusCode expected)
    {
        await SeedAsync(2);
        var before = await SnapshotAsync();
        using var client = fixture.Client(Permission(update), Resource, authority: authority);
        using var response = await SendAsync(client, update);
        fixture.IamTransportEvidence.AssertHealthy();
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
        Assert.DoesNotContain("private-marker", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    private static string Permission(bool update) => update ? "legacy-employee.signatures.write" : "legacy-employee.signatures.read";

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, bool update) => update
        ? client.PutAsJsonAsync(Route, new { Bucket = "replacement-bucket", ObjectName = "replacement.png" })
        : client.GetAsync(Route);

    private async Task SeedAsync(int matches)
    {
        await using var db = fixture.CreateContext();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"Employee\", \"Address\", \"Role\", \"SignatureImageFile\" RESTART IDENTITY CASCADE");
        db.Employees.AddRange(
            new Employee { Id = OwnerId, FirstName = "Owner", LastName = "Synthetic", Email = "owner@example.invalid" },
            new Employee { Id = OtherId, FirstName = "Other", LastName = "Synthetic", Email = "other@example.invalid" });
        for (var index = 0; index < matches; index++)
        {
            db.SignatureImageFiles.Add(new SignatureImageFile
            {
                Id = 101 + index,
                EmployeeId = OwnerId,
                Bucket = "signature-private-marker",
                ObjectName = $"owner-private-marker-{index}.png",
                CreatedDate = new DateTime(2020, 1, 1),
                ModifiedDate = new DateTime(2020, 1, 2)
            });
        }
        db.SignatureImageFiles.Add(new SignatureImageFile
        {
            Id = 301,
            EmployeeId = OtherId,
            Bucket = "signature-private-marker",
            ObjectName = "other-private-marker.png",
            CreatedDate = new DateTime(2020, 1, 1),
            ModifiedDate = new DateTime(2020, 1, 2)
        });
        await db.SaveChangesAsync();
        fixture.Authorities.Clear();
    }

    private async Task<string> SnapshotAsync()
    {
        await using var db = fixture.CreateContext();
        return JsonSerializer.Serialize(new
        {
            Employees = await db.Employees.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new { row.Id, row.FirstName, row.LastName, row.Email, row.CreatedDate, row.ModifiedDate }).ToArrayAsync(),
            Signatures = await db.SignatureImageFiles.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new { row.Id, row.EmployeeId, row.Bucket, row.ObjectName, row.CreatedDate, row.ModifiedDate }).ToArrayAsync()
        });
    }
}

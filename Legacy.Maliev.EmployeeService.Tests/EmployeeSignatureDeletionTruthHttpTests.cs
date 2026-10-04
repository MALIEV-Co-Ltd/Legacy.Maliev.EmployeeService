using System.Net;
using System.Text.Json;
using Legacy.Maliev.EmployeeService.Domain;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.EmployeeService.Tests;

public sealed class EmployeeSignatureDeletionTruthHttpTests(EmployeeRouteAcceptanceFixture fixture)
    : IClassFixture<EmployeeRouteAcceptanceFixture>
{
    private const int OwnerId = 17;
    private const int OtherId = 18;
    private const string Permission = "legacy-employee.signatures.delete";
    private const string Resource = "/employees/17/signature";
    private const string Route = "/employees/signatures/17";

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ExistingEmployeeMetadata_ReportsDeletionAndPreservesOtherEmployee(int matches)
    {
        await SeedAsync(matches);
        using var client = fixture.Client(Permission, Resource);

        using var deleted = await client.DeleteAsync(Route);
        fixture.IamTransportEvidence.AssertHealthy();

        await using (var db = fixture.CreateContext())
        {
            Assert.False(await db.SignatureImageFiles.AnyAsync(row => row.EmployeeId == OwnerId));
            var other = await db.SignatureImageFiles.SingleAsync();
            Assert.Equal(OtherId, other.EmployeeId);
            Assert.Equal("other-synthetic.png", other.ObjectName);
            Assert.Equal(2, await db.Employees.CountAsync());
        }
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(string.Empty, await deleted.Content.ReadAsStringAsync());

        var after = await SnapshotAsync();
        using var repeated = await client.DeleteAsync(Route);
        fixture.IamTransportEvidence.AssertHealthy();
        Assert.Equal(HttpStatusCode.NotFound, repeated.StatusCode);
        Assert.Equal(after, await SnapshotAsync());
        Assert.Equal(2, fixture.Authorities.Values.Single().LiveCalls);
    }

    [Theory]
    [InlineData("deny")]
    [InlineData("unavailable")]
    [InlineData("malformed")]
    [InlineData("no-client")]
    public async Task ForcedLiveRefusal_PreservesEveryDuplicateAndOtherEmployee(string decision)
    {
        await SeedAsync(2);
        using var host = decision == "no-client" ? fixture.NewFactory(withIam: false) : null;
        using var client = fixture.Client(Permission, Resource, decision: decision, factory: host);
        var before = await SnapshotAsync();

        using var response = await client.DeleteAsync(Route);
        fixture.IamTransportEvidence.AssertHealthy();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
        Assert.DoesNotContain("synthetic.png", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("anonymous", HttpStatusCode.Unauthorized)]
    [InlineData("wrong-signature", HttpStatusCode.Unauthorized)]
    [InlineData("missing-permission", HttpStatusCode.Forbidden)]
    public async Task InvalidAuthority_CannotDeleteDuplicateMetadata(string authority, HttpStatusCode expected)
    {
        await SeedAsync(2);
        using var client = fixture.Client(Permission, Resource, authority: authority);
        var before = await SnapshotAsync();

        using var response = await client.DeleteAsync(Route);
        fixture.IamTransportEvidence.AssertHealthy();

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
    }

    private async Task SeedAsync(int matches)
    {
        await using var db = fixture.CreateContext();
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE \"Employee\", \"Address\", \"Role\", \"SignatureImageFile\" RESTART IDENTITY CASCADE");
        db.Employees.AddRange(
            new Employee { Id = OwnerId, FirstName = "Owner", LastName = "Synthetic", Email = "owner@example.invalid" },
            new Employee { Id = OtherId, FirstName = "Other", LastName = "Synthetic", Email = "other@example.invalid" });
        for (var index = 0; index < matches; index++)
        {
            db.SignatureImageFiles.Add(new SignatureImageFile
            {
                Id = 101 + index,
                EmployeeId = OwnerId,
                Bucket = "synthetic-private",
                ObjectName = "owner-" + index + "-synthetic.png"
            });
        }
        db.SignatureImageFiles.Add(new SignatureImageFile
        {
            Id = 301,
            EmployeeId = OtherId,
            Bucket = "synthetic-private",
            ObjectName = "other-synthetic.png"
        });
        await db.SaveChangesAsync();
        Assert.Equal(matches, await db.SignatureImageFiles.CountAsync(row => row.EmployeeId == OwnerId));
        fixture.Authorities.Clear();
    }

    private async Task<string> SnapshotAsync()
    {
        await using var db = fixture.CreateContext();
        return JsonSerializer.Serialize(new
        {
            Employees = await db.Employees.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new { row.Id, row.FirstName, row.LastName, row.Email, row.ModifiedDate }).ToArrayAsync(),
            Signatures = await db.SignatureImageFiles.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new { row.Id, row.EmployeeId, row.Bucket, row.ObjectName, row.CreatedDate, row.ModifiedDate }).ToArrayAsync()
        });
    }
}

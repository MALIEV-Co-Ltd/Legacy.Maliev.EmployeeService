using System.Net;
using System.Data.Common;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.EmployeeService.Application.Models;
using Legacy.Maliev.EmployeeService.Application.Interfaces;
using Legacy.Maliev.EmployeeService.Application.Services;
using Legacy.Maliev.EmployeeService.Data;
using Legacy.Maliev.EmployeeService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Legacy.Maliev.EmployeeService.Tests;

[CollectionDefinition("Employee conditional edits", DisableParallelization = true)]
public sealed class EmployeeConditionalEditCollection;

[Collection("Employee conditional edits")]
public sealed class EmployeeConditionalEditHttpTests(EmployeeRouteAcceptanceFixture fixture) : IClassFixture<EmployeeRouteAcceptanceFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TwoReaders_SecondSaveIsStaleAndPreservesWholeGraphAndRedis(bool address)
    {
        await ResetAsync();
        using var reader = ReadClient(address);
        using var firstRead = await reader.GetAsync(ReadPath(address));
        using var secondRead = await reader.GetAsync(ReadPath(address));
        Assert.Equal(HttpStatusCode.OK, firstRead.StatusCode);
        Assert.Equal(firstRead.Headers.ETag, secondRead.Headers.ETag);
        Assert.Equal("no-store", firstRead.Headers.CacheControl!.ToString());
        var version = firstRead.Headers.ETag!.ToString();
        Assert.True(EmployeeEditVersion.IsValid(version));
        using var writer = WriteClient(address);
        using var saved = await PutAsync(writer, address, version, "First writer");
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        Assert.NotEqual(version, saved.Headers.ETag!.ToString());
        using var afterRead = await reader.GetAsync(ReadPath(address));
        Assert.Equal(saved.Headers.ETag, afterRead.Headers.ETag);
        await SeedCacheAsync();
        var before = await SnapshotAsync();
        using var stale = await PutAsync(writer, address, version, "Stale writer");
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
        await AssertCacheAsync(true);
        using var savedAgain = await PutAsync(writer, address, saved.Headers.ETag!.ToString(), "Fresh writer");
        Assert.Equal(HttpStatusCode.NoContent, savedAgain.StatusCode);
        await AssertCacheAsync(false, address);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentConditionalSaves_ExactlyOneWins(bool address)
    {
        await ResetAsync();
        using var reader = ReadClient(address);
        using var read = await reader.GetAsync(ReadPath(address));
        var version = read.Headers.ETag!.ToString();
        using var writerA = WriteClient(address);
        using var writerB = WriteClient(address);
        var results = await Task.WhenAll(PutAsync(writerA, address, version, "Writer A"), PutAsync(writerB, address, version, "Writer B"));
        try
        {
            Assert.Single(results, item => item.StatusCode == HttpStatusCode.NoContent);
            Assert.Single(results, item => item.StatusCode == HttpStatusCode.PreconditionFailed);
        }
        finally { foreach (var response in results) response.Dispose(); }
    }

    [Theory]
    [InlineData(false, "*")]
    [InlineData(true, "*")]
    [InlineData(false, "W/\"weak\"")]
    [InlineData(true, "W/\"weak\"")]
    [InlineData(false, "\"first\", \"second\"")]
    [InlineData(true, "\"first\", \"second\"")]
    [InlineData(false, "not-a-version")]
    [InlineData(true, "not-a-version")]
    public async Task MalformedOrBypassPrecondition_Is400WithoutMutation(bool address, string version)
    {
        await ResetAsync();
        await SeedCacheAsync();
        var before = await SnapshotAsync();
        using var writer = WriteClient(address);
        using var response = await PutAsync(writer, address, version, "Malformed version");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
        await AssertCacheAsync(true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WrongResourceVersion_Is412WithoutMutation(bool address)
    {
        await ResetAsync();
        using var otherReader = fixture.Client("legacy-employee.employees.read", "/employees/3");
        using var other = await otherReader.GetAsync("/employees/3/edit");
        var before = await SnapshotAsync();
        using var writer = WriteClient(address);
        using var rejected = await PutAsync(writer, address, other.Headers.ETag!.ToString(), "Wrong resource");
        Assert.Equal(HttpStatusCode.PreconditionFailed, rejected.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingRows_Keep404WithWellFormedVersion(bool address)
    {
        await ResetAsync();
        using var writer = fixture.Client(address ? "legacy-employee.addresses.update" : "legacy-employee.employees.update",
            address ? "/employees/addresses/999" : "/employees/999");
        using var request = new HttpRequestMessage(HttpMethod.Put, address ? "/employees/addresses/999/versioned" : "/employees/999/versioned")
        { Content = JsonContent.Create(Payload(address, "Missing")) };
        request.Headers.TryAddWithoutValidation("If-Match", '"' + new string('0', 64) + '"');
        using var response = await writer.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var reader = fixture.Client(address ? "legacy-employee.addresses.read" : "legacy-employee.employees.read",
            address ? "/employees/addresses/999" : "/employees/999");
        using var missing = await reader.GetAsync(address ? "/employees/addresses/999" : "/employees/999/edit");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Theory]
    [InlineData(false, "anonymous", HttpStatusCode.Unauthorized)]
    [InlineData(true, "anonymous", HttpStatusCode.Unauthorized)]
    [InlineData(false, "missing-permission", HttpStatusCode.Forbidden)]
    [InlineData(true, "missing-permission", HttpStatusCode.Forbidden)]
    public async Task AuthorizationRefusal_PrecedesConditionalMutation(bool address, string authority, HttpStatusCode expected)
    {
        await ResetAsync();
        using var reader = ReadClient(address);
        using var read = await reader.GetAsync(ReadPath(address));
        var before = await SnapshotAsync();
        using var writer = WriteClient(address, authority);
        using var response = await PutAsync(writer, address, read.Headers.ETag!.ToString(), "Unauthorized");
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public async Task ProfileEditProjection_ExcludesRelatedAndIdentityFieldsAndRetainsPascalCase()
    {
        await ResetAsync();
        using var reader = ReadClient(false);
        using var read = await reader.GetAsync(ReadPath(false));
        using var document = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.Equal(1, document.RootElement.GetProperty("Id").GetInt32());
        foreach (var field in new[] { "HomeAddress", "Role", "SecurityStamp", "ConcurrencyStamp", "PasswordHash", "AccessFailedCount", "DatabaseID", "firstName" })
            Assert.False(document.RootElement.TryGetProperty(field, out _));
        using var addressReader = ReadClient(true);
        using var address = await addressReader.GetAsync(ReadPath(true));
        using var addressWriter = WriteClient(true);
        using var addressSaved = await PutAsync(addressWriter, true, address.Headers.ETag!.ToString(), "Related address change");
        Assert.Equal(HttpStatusCode.NoContent, addressSaved.StatusCode);
        using var reread = await reader.GetAsync(ReadPath(false));
        Assert.Equal(read.Headers.ETag, reread.Headers.ETag);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnversionedPut_RemainsCompatibleAndMakesOldEditStale(bool address)
    {
        await ResetAsync();
        using var reader = ReadClient(address);
        using var read = await reader.GetAsync(ReadPath(address));
        using var writer = WriteClient(address);
        using var unversioned = await PutAsync(writer, address, null, "Legacy writer");
        Assert.Equal(HttpStatusCode.NoContent, unversioned.StatusCode);
        using var stale = await PutAsync(writer, address, read.Headers.ETag!.ToString(), "Old editor");
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidProfileOrMissingRelationship_PreservesStoredRowsAndRedis(bool missingRelationship)
    {
        await ResetAsync();
        using var reader = ReadClient(false);
        using var read = await reader.GetAsync(ReadPath(false));
        await SeedCacheAsync();
        var before = await SnapshotAsync();
        using var writer = WriteClient(false);
        using var request = new HttpRequestMessage(HttpMethod.Put, "/employees/1/versioned")
        {
            Content = JsonContent.Create(new UpsertEmployeeRequest(null, missingRelationship ? "Valid" : "", "Fixture",
                null, "valid@example.test", null, missingRelationship ? 999 : 1)),
        };
        request.Headers.TryAddWithoutValidation("If-Match", read.Headers.ETag!.ToString());
        using var response = await writer.SendAsync(request);
        Assert.Equal(missingRelationship ? HttpStatusCode.InternalServerError : HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
        await AssertCacheAsync(true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingVersionOnVersionedRoute_Is428WithoutMutation(bool address)
    {
        await ResetAsync();
        var before = await SnapshotAsync();
        using var writer = WriteClient(address);
        using var response = await writer.PutAsJsonAsync(address ? "/employees/addresses/1/versioned" : "/employees/1/versioned", Payload(address, "No version"));
        Assert.Equal((HttpStatusCode)428, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LostCommitAcknowledgment_IsUncertain500WithOneCommitAndSafeCacheInvalidation(bool address)
    {
        await ResetAsync();
        using var reader = ReadClient(address);
        using var read = await reader.GetAsync(ReadPath(address));
        await SeedCacheAsync();
        var fault = new CommitAcknowledgmentFault();
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<EmployeeDbContext>(options => options.AddInterceptors(fault))));
        using var writer = fixture.Client(address ? "legacy-employee.addresses.update" : "legacy-employee.employees.update",
            address ? "/employees/addresses/1" : "/employees/1", factory: factory);
        using var response = await PutAsync(writer, address, read.Headers.ETag!.ToString(), "Committed once");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(1, fault.Commits);
        using var after = await reader.GetAsync(ReadPath(address));
        Assert.NotEqual(read.Headers.ETag, after.Headers.ETag);
        using var document = JsonDocument.Parse(await after.Content.ReadAsStringAsync());
        Assert.Equal("Committed once", document.RootElement.GetProperty(address ? "AddressLine1" : "FirstName").GetString());
        await AssertCacheAsync(false, address);
        Assert.DoesNotContain("synthetic-commit-ack", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerCancellationAtCommit_IsPreservedWithoutRetryAndEvictsPotentiallyStaleCache(bool address)
    {
        await ResetAsync();
        using var reader = ReadClient(address);
        using var read = await reader.GetAsync(ReadPath(address));
        await SeedCacheAsync();
        using var cancellation = new CancellationTokenSource();
        var fault = new CommitAcknowledgmentFault(cancellation);
        await using var original = fixture.CreateContext();
        var options = new DbContextOptionsBuilder<EmployeeDbContext>(original.GetService<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptions>() as DbContextOptions<EmployeeDbContext>
            ?? throw new InvalidOperationException("Fixture context options unavailable"))
            .AddInterceptors(fault).Options;
        await using var context = new EmployeeDbContext(options);
        using var scope = fixture.Factory.Services.CreateScope();
        var service = new EmployeeApplicationService(new EmployeeRepository(context, TimeProvider.System), scope.ServiceProvider.GetRequiredService<IEmployeeCache>());
        var token = read.Headers.ETag!.ToString();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            if (address) await service.UpdateAddressIfMatchAsync(1, (UpsertAddressRequest)Payload(true, "Canceled after commit"), token, cancellation.Token);
            else await service.UpdateEmployeeIfMatchAsync(1, (UpsertEmployeeRequest)Payload(false, "Canceled after commit"), token, cancellation.Token);
        });
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(1, fault.Commits);
        using var after = await reader.GetAsync(ReadPath(address));
        Assert.NotEqual(read.Headers.ETag, after.Headers.ETag);
        await AssertCacheAsync(false, address);
    }

    private HttpClient ReadClient(bool address) => fixture.Client(address ? "legacy-employee.addresses.read" : "legacy-employee.employees.read",
        address ? "/employees/addresses/1" : "/employees/1");

    private HttpClient WriteClient(bool address, string authority = "valid") => fixture.Client(
        address ? "legacy-employee.addresses.update" : "legacy-employee.employees.update",
        address ? "/employees/addresses/1" : "/employees/1", authority);

    private static string ReadPath(bool address) => address ? "/employees/addresses/1" : "/employees/1/edit";

    private static object Payload(bool address, string text) => address
        ? new UpsertAddressRequest(null, text, null, null, null, null, 764)
        : new UpsertEmployeeRequest(null, text, "Fixture", null, "edit@example.test", null, 1);

    private static async Task<HttpResponseMessage> PutAsync(HttpClient client, bool address, string? version, string text)
    {
        var path = address ? "/employees/addresses/1" : "/employees/1";
        if (version is not null) path += "/versioned";
        using var request = new HttpRequestMessage(HttpMethod.Put, path)
        { Content = JsonContent.Create(Payload(address, text)) };
        if (version is not null) request.Headers.TryAddWithoutValidation("If-Match", version);
        return await client.SendAsync(request);
    }

    private async Task ResetAsync()
    {
        fixture.Authorities.Clear();
        await using var db = fixture.CreateContext();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"Employee\", \"Address\", \"Role\", \"SignatureImageFile\" RESTART IDENTITY CASCADE");
        db.Addresses.Add(new Address { AddressLine1 = "Original", CountryId = 764 });
        await db.SaveChangesAsync();
        db.Employees.AddRange(
            new Employee { FirstName = "First", LastName = "Fixture", Email = "first@example.test", HomeAddressId = 1 },
            new Employee { FirstName = "Second", LastName = "Fixture", Email = "second@example.test", HomeAddressId = 1 },
            new Employee { FirstName = "Third", LastName = "Fixture", Email = "third@example.test" });
        await db.SaveChangesAsync();
        using var scope = fixture.Factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        for (var id = 1; id <= 3; id++) await cache.RemoveAsync($"employee:{id}");
    }

    private async Task<string> SnapshotAsync()
    {
        await using var db = fixture.CreateContext();
        return JsonSerializer.Serialize(new
        {
            employees = await db.Employees.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new
                {
                    row.Id,
                    row.FirstName,
                    row.LastName,
                    row.FullName,
                    row.Email,
                    row.PhoneNumber,
                    row.DateOfBirth,
                    row.RoleId,
                    row.HomeAddressId,
                    row.CreatedDate,
                    row.ModifiedDate,
                }).ToArrayAsync(),
            addresses = await db.Addresses.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new
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
                }).ToArrayAsync(),
            roles = await db.Roles.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new
                {
                    row.Id,
                    row.Name,
                    row.Description,
                    row.CreatedDate,
                    row.ModifiedDate,
                }).ToArrayAsync(),
            signatures = await db.SignatureImageFiles.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new
                {
                    row.Id,
                    row.EmployeeId,
                    row.Bucket,
                    row.ObjectName,
                    row.CreatedDate,
                    row.ModifiedDate,
                }).ToArrayAsync(),
        });
    }

    private async Task SeedCacheAsync()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        for (var id = 1; id <= 3; id++) await cache.SetAsync($"employee:{id}", Encoding.UTF8.GetBytes("sentinel"));
    }

    private async Task AssertCacheAsync(bool retained, bool address = false)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        for (var id = 1; id <= 3; id++)
        {
            var expected = retained || id == 3 || (!address && id == 2);
            if (expected) Assert.NotNull(await cache.GetAsync($"employee:{id}"));
            else Assert.Null(await cache.GetAsync($"employee:{id}"));
        }
    }

    private sealed class CommitAcknowledgmentFault(CancellationTokenSource? cancellation = null) : DbTransactionInterceptor
    {
        private int commits;
        internal int Commits => Volatile.Read(ref commits);

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref commits) == 1)
            {
                if (cancellation is not null)
                {
                    cancellation.Cancel();
                    throw new OperationCanceledException(cancellation.Token);
                }
                throw new NpgsqlException("synthetic-commit-ack", new TimeoutException("synthetic-commit-ack"));
            }
            return Task.CompletedTask;
        }
    }
}

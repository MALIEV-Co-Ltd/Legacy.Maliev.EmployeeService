using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.EmployeeService.Application.Interfaces;
using Legacy.Maliev.EmployeeService.Application.Models;
using Legacy.Maliev.EmployeeService.Application.Services;
using Legacy.Maliev.EmployeeService.Data;
using Legacy.Maliev.EmployeeService.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Legacy.Maliev.EmployeeService.Tests;

[Collection("Employee conditional edits")]
public sealed class EmployeeHomeAddressConditionalEditHttpTests(EmployeeRouteAcceptanceFixture fixture) : IClassFixture<EmployeeRouteAcceptanceFixture>
{
    private const string EmployeePermission = "legacy-employee.employees.update";
    private const string AddressPermission = "legacy-employee.addresses.update";

    [Fact]
    public async Task BoundEdit_PreservesEmployeeVersionAndFieldsAndEvictsSharedCaches()
    {
        await ResetAsync();
        var versions = await VersionsAsync();
        await SeedCacheAsync();
        await using var db = fixture.CreateContext();
        var beforeEmployee = await db.Employees.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync();
        using var writer = Writer();
        using var result = await PutAsync(writer, versions.Employee, versions.Address);
        Assert.Equal(HttpStatusCode.NoContent, result.StatusCode);
        Assert.NotEqual(versions.Address, result.Headers.ETag!.ToString());
        Assert.Equal(versions.Employee, result.Headers.GetValues("X-Employee-ETag").Single());
        Assert.Equal(JsonSerializer.Serialize(beforeEmployee), JsonSerializer.Serialize(await db.Employees.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync()));
        Assert.Equal("Changed home address", (await db.Addresses.AsNoTracking().SingleAsync(row => row.Id == 1)).AddressLine1);
        Assert.Equal("Other address", (await db.Addresses.AsNoTracking().SingleAsync(row => row.Id == 2)).AddressLine1);
        await AssertCacheAsync(false, true);
        var after = await VersionsAsync();
        Assert.Equal(versions.Employee, after.Employee);
        Assert.Equal(result.Headers.ETag.ToString(), after.Address);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EitherIndependentlyChangedVersion_Is412WithoutMutation(bool address)
    {
        await ResetAsync();
        var versions = await VersionsAsync();
        using var otherWriter = fixture.Client(address ? AddressPermission : EmployeePermission,
            address ? "/employees/addresses/1" : "/employees/1");
        using var changed = address
            ? await otherWriter.PutAsJsonAsync("/employees/addresses/1", Payload().Address())
            : await otherWriter.PutAsJsonAsync("/employees/1", Profile(1, "Changed profile"));
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        await SeedCacheAsync();
        var before = await SnapshotAsync();
        using var writer = Writer();
        using var rejected = await PutAsync(writer, versions.Employee, versions.Address);
        Assert.Equal(HttpStatusCode.PreconditionFailed, rejected.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
        await AssertCacheAsync(true);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(2)]
    public async Task RelocatedOrDetachedBinding_Is412WithoutChangingEitherAddress(int? movedAddress)
    {
        await ResetAsync();
        var versions = await VersionsAsync();
        using var mover = fixture.Client(EmployeePermission, "/employees/1");
        using var moved = await mover.PutAsJsonAsync("/employees/1", Profile(movedAddress));
        Assert.Equal(HttpStatusCode.NoContent, moved.StatusCode);
        var freshProfile = await VersionsAsync();
        var before = await SnapshotAsync();
        using var writer = Writer();
        // Even a freshly reread employee version cannot authorize an address no longer bound to it.
        using var rejected = await PutAsync(writer, freshProfile.Employee, versions.Address);
        Assert.Equal(HttpStatusCode.PreconditionFailed, rejected.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public async Task ConcurrentOrdinaryRelationMove_BeforeEmployeeFence_Is412AndMutatesNoAddress()
    {
        await ResetAsync();
        var versions = await VersionsAsync();
        var gate = new EmployeeFenceGate();
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<EmployeeDbContext>(options => options.AddInterceptors(gate))));
        using var writer = Writer(factory: factory);
        var save = PutAsync(writer, versions.Employee, versions.Address);
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            using var mover = fixture.Client(EmployeePermission, "/employees/1");
            using var moved = await mover.PutAsJsonAsync("/employees/1", Profile(2));
            Assert.Equal(HttpStatusCode.NoContent, moved.StatusCode);
            var afterMove = await SnapshotAsync();
            gate.Release.TrySetResult();
            using var rejected = await save;
            Assert.Equal(HttpStatusCode.PreconditionFailed, rejected.StatusCode);
            Assert.Equal(afterMove, await SnapshotAsync());
        }
        finally { gate.Release.TrySetResult(); }
    }

    [Fact]
    public async Task ConcurrentBoundAddressSaves_ExactlyOneWins()
    {
        await ResetAsync();
        var versions = await VersionsAsync();
        using var first = Writer();
        using var second = Writer();
        var results = await Task.WhenAll(PutAsync(first, versions.Employee, versions.Address), PutAsync(second, versions.Employee, versions.Address));
        try
        {
            Assert.Single(results, result => result.StatusCode == HttpStatusCode.NoContent);
            Assert.Single(results, result => result.StatusCode == HttpStatusCode.PreconditionFailed);
        }
        finally { foreach (var result in results) result.Dispose(); }
    }

    [Fact]
    public async Task EmployeeFence_HoldsUntilAddressCommitAndBlocksOrdinaryRelationMove()
    {
        await ResetAsync();
        var versions = await VersionsAsync();
        var gate = new HeldEmployeeFenceGate();
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<EmployeeDbContext>(options => options.AddInterceptors(gate))));
        using var writer = Writer(factory: factory);
        using var mover = fixture.Client(EmployeePermission, "/employees/1");
        var save = PutAsync(writer, versions.Employee, versions.Address);
        Task<HttpResponseMessage>? move = null;
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            move = mover.PutAsJsonAsync("/employees/1", Profile(2));
            await using var observer = fixture.CreateContext();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (await observer.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*)::int AS \"Value\" FROM pg_stat_activity WHERE wait_event_type = 'Lock' AND query LIKE '%UPDATE \"Employee\"%'")
                .SingleAsync(timeout.Token) == 0)
                await Task.Delay(25, timeout.Token);
            Assert.False(move.IsCompleted);
            gate.Release.TrySetResult();
            using var saved = await save;
            using var moved = await move;
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, moved.StatusCode);
            await using var db = fixture.CreateContext();
            Assert.Equal(2, (await db.Employees.SingleAsync(row => row.Id == 1)).HomeAddressId);
            Assert.Equal("Changed home address", (await db.Addresses.SingleAsync(row => row.Id == 1)).AddressLine1);
            Assert.Equal("Other address", (await db.Addresses.SingleAsync(row => row.Id == 2)).AddressLine1);
        }
        finally
        {
            gate.Release.TrySetResult();
            if (!save.IsCompleted) (await save).Dispose();
            if (move is not null && !move.IsCompleted) (await move).Dispose();
        }
    }

    [Theory]
    [InlineData(null, "valid", 428)]
    [InlineData("valid", null, 428)]
    [InlineData("*", "valid", 400)]
    [InlineData("valid", "*", 400)]
    [InlineData("W/\"weak\"", "valid", 400)]
    [InlineData("valid", "W/\"weak\"", 400)]
    [InlineData("\"first\", \"second\"", "valid", 400)]
    [InlineData("valid", "\"first\", \"second\"", 400)]
    [InlineData("swapped", "valid", 412)]
    [InlineData("valid", "swapped", 412)]
    public async Task BothStrongPreconditions_AreRequiredAndResourceBound(string? employee, string? address, int status)
    {
        await ResetAsync();
        var versions = await VersionsAsync();
        var before = await SnapshotAsync();
        using var writer = Writer();
        using var result = await PutAsync(writer,
            employee == "valid" ? versions.Employee : employee == "swapped" ? versions.Address : employee,
            address == "valid" ? versions.Address : address == "swapped" ? versions.Employee : address);
        Assert.Equal((HttpStatusCode)status, result.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Theory]
    [InlineData("anonymous", 401)]
    [InlineData("employee-only", 403)]
    [InlineData("address-only", 403)]
    public async Task BothPermissions_AreRequired(string authority, int status)
    {
        await ResetAsync();
        var versions = await VersionsAsync();
        var before = await SnapshotAsync();
        using var writer = Writer(authority);
        using var result = await PutAsync(writer, versions.Employee, versions.Address);
        Assert.Equal((HttpStatusCode)status, result.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public async Task WrongCapturedAddressId_Is412EvenWithBroadAddressPermission()
    {
        await ResetAsync();
        var versions = await VersionsAsync();
        var before = await SnapshotAsync();
        using var writer = Writer(addressId: 2);
        using var result = await PutAsync(writer, versions.Employee, versions.Address, Payload() with { AddressId = 2 });
        Assert.Equal(HttpStatusCode.PreconditionFailed, result.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public async Task AbsentEmployee_Is404WithoutAddressMutation()
    {
        await ResetAsync();
        var versions = await VersionsAsync();
        var before = await SnapshotAsync();
        using var writer = Writer(employeeId: 999);
        using var result = await PutAsync(writer, versions.Employee, versions.Address, employeeId: 999);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public async Task UnknownProfileFields_Are400WithoutMassAssignment()
    {
        await ResetAsync();
        var versions = await VersionsAsync();
        var before = await SnapshotAsync();
        using var writer = Writer();
        using var request = new HttpRequestMessage(HttpMethod.Put, "/employees/1/home-address/versioned")
        { Content = JsonContent.Create(new { AddressId = 1, AddressLine1 = "Changed", CountryId = 764, HomeAddressId = 2 }) };
        request.Headers.TryAddWithoutValidation("If-Match", versions.Address);
        request.Headers.TryAddWithoutValidation("X-Employee-If-Match", versions.Employee);
        using var result = await writer.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task InvalidCapturedAddressId_Is400WithoutMutation(int addressId)
    {
        await ResetAsync();
        var versions = await VersionsAsync();
        var before = await SnapshotAsync();
        using var writer = Writer(addressId: addressId);
        using var result = await PutAsync(writer, versions.Employee, versions.Address, Payload() with { AddressId = addressId });
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public async Task LostCommitAcknowledgment_IsUncertain500WithOneCommitAndLinkedCacheEviction()
    {
        await ResetAsync();
        var versions = await VersionsAsync();
        await SeedCacheAsync();
        var fault = new CommitAcknowledgmentFault();
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<EmployeeDbContext>(options => options.AddInterceptors(fault))));
        using var writer = Writer(factory: factory);
        using var result = await PutAsync(writer, versions.Employee, versions.Address);
        Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode);
        Assert.Equal(1, fault.Commits);
        var after = await VersionsAsync();
        Assert.Equal(versions.Employee, after.Employee);
        Assert.NotEqual(versions.Address, after.Address);
        await AssertCacheAsync(false, true);
        Assert.DoesNotContain("synthetic-commit-ack", await result.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CommitCancellation_PreservesCallerTokenAndEvictsLinkedCachesWithoutReplay()
    {
        await ResetAsync();
        var versions = await VersionsAsync();
        await SeedCacheAsync();
        using var cancellation = new CancellationTokenSource();
        var fault = new CommitAcknowledgmentFault(cancellation);
        await using var original = fixture.CreateContext();
        var options = new DbContextOptionsBuilder<EmployeeDbContext>((DbContextOptions<EmployeeDbContext>)original.GetService<IDbContextOptions>())
            .AddInterceptors(fault).Options;
        await using var context = new EmployeeDbContext(options);
        using var scope = fixture.Factory.Services.CreateScope();
        var service = new EmployeeApplicationService(new EmployeeRepository(context, TimeProvider.System), scope.ServiceProvider.GetRequiredService<IEmployeeCache>());
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.UpdateHomeAddressIfMatchAsync(
            1, Payload(), versions.Employee, versions.Address, cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(1, fault.Commits);
        Assert.NotEqual(versions.Address, (await VersionsAsync()).Address);
        await AssertCacheAsync(false, true);
    }

    private HttpClient Writer(string authority = "valid", WebApplicationFactory<Program>? factory = null, int employeeId = 1, int addressId = 1) =>
        fixture.Client(EmployeePermission, $"/employees/{employeeId}", authority == "anonymous" ? "anonymous" : "valid", factory: factory,
            additionalPermissions: new Dictionary<string, string> { [AddressPermission] = $"/employees/addresses/{addressId}" },
            permissionClaims: authority switch
            {
                "employee-only" => [EmployeePermission],
                "address-only" => [AddressPermission],
                _ => [EmployeePermission, AddressPermission],
            });

    private async Task<(string Employee, string Address)> VersionsAsync()
    {
        using var profile = fixture.Client("legacy-employee.employees.read", "/employees/1");
        using var address = fixture.Client("legacy-employee.addresses.read", "/employees/addresses/1");
        using var first = await profile.GetAsync("/employees/1/edit");
        using var second = await address.GetAsync("/employees/addresses/1");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        return (first.Headers.ETag!.ToString(), second.Headers.ETag!.ToString());
    }

    private static UpsertEmployeeRequest Profile(int? address, string firstName = "First") =>
        new(null, firstName, "Fixture", null, "first@example.test", null, address);

    private static EmployeeHomeAddressEditRequest Payload() => new(1, null, "Changed home address", null, null, null, null, 764);

    private static async Task<HttpResponseMessage> PutAsync(HttpClient client, string? employeeVersion, string? addressVersion,
        EmployeeHomeAddressEditRequest? payload = null, int employeeId = 1)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/employees/{employeeId}/home-address/versioned")
        { Content = JsonContent.Create(payload ?? Payload()) };
        if (employeeVersion is not null) request.Headers.TryAddWithoutValidation("X-Employee-If-Match", employeeVersion);
        if (addressVersion is not null) request.Headers.TryAddWithoutValidation("If-Match", addressVersion);
        return await client.SendAsync(request);
    }

    private sealed class EmployeeFenceGate : DbCommandInterceptor
    {
        private int entries;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"Employee\"", StringComparison.Ordinal) &&
                command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal) && Interlocked.Increment(ref entries) == 1)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    private sealed class HeldEmployeeFenceGate : DbCommandInterceptor
    {
        private int entries;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"Employee\"", StringComparison.Ordinal) &&
                command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal) && Interlocked.Increment(ref entries) == 1)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
    private async Task ResetAsync()
    {
        fixture.Authorities.Clear();
        await using var db = fixture.CreateContext();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"Employee\", \"Address\", \"Role\", \"SignatureImageFile\" RESTART IDENTITY CASCADE");
        db.Addresses.AddRange(new Address { AddressLine1 = "Original", CountryId = 764 },
            new Address { AddressLine1 = "Other address", CountryId = 764 });
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

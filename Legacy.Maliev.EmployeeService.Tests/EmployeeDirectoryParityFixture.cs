using System.IdentityModel.Tokens.Jwt;
using System.Data.Common;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Legacy.Maliev.EmployeeService.Api.Authorization;
using Legacy.Maliev.EmployeeService.Data;
using Legacy.Maliev.EmployeeService.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;

namespace Legacy.Maliev.EmployeeService.Tests;

/// <summary>Owns PostgreSQL and Redis containers, signing keys, and a production HTTP host for directory parity tests.</summary>
public sealed class EmployeeDirectoryParityFixture : IAsyncLifetime
{
    private const string Issuer = "https://employee-directory.example.invalid";
    private const string Audience = "employee-directory-parity";
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithName($"employee-directory-pg-{Guid.NewGuid():N}").Build();
    private readonly IContainer redis = new ContainerBuilder("redis:7.4-alpine")
        .WithName($"employee-directory-redis-{Guid.NewGuid():N}")
        .WithPortBinding(6379, true).WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(6379)).Build();
    private readonly RSA rsa = RSA.Create(2048);
    private readonly RSA otherRsa = RSA.Create(2048);
    /// <summary>The production test host configured with the fixture database, Redis cache, public signing key, and cancellation interceptor.</summary>
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    /// <summary>The interceptor gate used to pause an employee query until its request is cancelled.</summary>
    public DirectoryCancellationGate CancellationGate { get; } = new();

    /// <summary>Starts the owned database and cache, applies migrations, and constructs the directory test host.</summary>
    /// <returns>A task that completes when the fixture containers, migrated database, and HTTP host factory are ready.</returns>
    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        await redis.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
        Factory = new DirectoryFactory(postgres.GetConnectionString(),
            $"{redis.Hostname}:{redis.GetMappedPublicPort(6379)}", rsa, CancellationGate);
    }

    /// <summary>Disposes the host, signing keys, Redis container, and PostgreSQL container owned by this fixture.</summary>
    /// <returns>A task that completes after all fixture-owned disposable resources have been released.</returns>
    public async Task DisposeAsync()
    {
        if (Factory is not null) await Factory.DisposeAsync();
        rsa.Dispose();
        otherRsa.Dispose();
        await redis.DisposeAsync();
        await postgres.DisposeAsync();
    }

    /// <summary>Creates a directory HTTP client with a signed permission token or the selected authentication failure scenario.</summary>
    /// <param name="authority">The valid, anonymous, expired, missing-permission, or wrong-signature scenario used to construct the client token.</param>
    /// <returns>A caller-owned HTTP client targeting the fixture host with the requested authentication scenario.</returns>
    public HttpClient CreateClient(string authority = "valid")
    {
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (authority == "anonymous") return client;
        var now = DateTime.UtcNow;
        var expired = authority == "expired";
        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, "service:legacy-intranet") };
        if (authority != "missing-permission") claims.Add(new Claim("permissions", EmployeePermissions.EmployeesList));
        var token = new JwtSecurityToken(Issuer, Audience, claims,
            expired ? now.AddHours(-1) : now.AddMinutes(-1),
            expired ? now.AddMinutes(-10) : now.AddMinutes(5),
            new SigningCredentials(new RsaSecurityKey(authority == "wrong-signature" ? otherRsa : rsa), SecurityAlgorithms.RsaSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        return client;
    }

    /// <summary>Replaces the employee graph with the supplied directory rows and optional home addresses.</summary>
    /// <param name="rows">The synthetic employee identifiers, literal profile values, and address-presence flags to persist after clearing the fixture tables.</param>
    /// <returns>A task that completes after the replacement employee rows and optional addresses have been saved.</returns>
    public async Task SeedAsync(params DirectoryRow[] rows)
    {
        await using var db = CreateDbContext();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"Employee\", \"Address\", \"Role\", \"SignatureImageFile\" RESTART IDENTITY CASCADE");
        foreach (var row in rows)
        {
            var address = row.Address ? new Address { AddressLine1 = "Thai fixture road", CountryId = 764 } : null;
            db.Employees.Add(new Employee
            {
                Id = row.Id,
                FirstName = row.FirstName,
                LastName = row.LastName,
                Email = row.Email,
                PhoneNumber = row.Phone,
                HomeAddress = address,
                CreatedDate = new DateTime(2020, 1, 1),
                ModifiedDate = new DateTime(2020, 1, 2),
            });
        }
        await db.SaveChangesAsync();
    }

    /// <summary>Captures ordered employee profile and home-address values for mutation checks.</summary>
    /// <returns>A task producing a JSON snapshot of employee profile rows and their fixture address rows.</returns>
    public async Task<string> SnapshotAsync()
    {
        await using var db = CreateDbContext();
        var rows = await db.Employees.AsNoTracking().OrderBy(value => value.Id).Select(value => new
        {
            value.Id,
            value.FirstName,
            value.LastName,
            value.Email,
            value.PhoneNumber,
            value.FullName,
            value.HomeAddressId,
            value.RoleId,
            value.CreatedDate,
            value.ModifiedDate,
        }).ToArrayAsync();
        var addresses = await db.Addresses.AsNoTracking().OrderBy(value => value.Id).Select(value => new
        {
            value.Id,
            value.AddressLine1,
            value.CountryId,
        }).ToArrayAsync();
        return JsonSerializer.Serialize(new { rows, addresses });
    }

    private EmployeeDbContext CreateDbContext() => new(new DbContextOptionsBuilder<EmployeeDbContext>()
        .UseNpgsql(postgres.GetConnectionString()).Options);

    private sealed class DirectoryFactory(string postgresConnection, string redisConnection, RSA key, DirectoryCancellationGate gate) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:EmployeeDbContext"] = postgresConnection,
                ["ConnectionStrings:redis"] = redisConnection,
                ["Cache:RedisEnabled"] = "true",
                ["Jwt:PublicKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(key.ExportSubjectPublicKeyInfoPem())),
                ["Jwt:Issuer"] = Issuer,
                ["Jwt:Audience"] = Audience,
                ["Logging:LogLevel:Default"] = "Warning",
            };
            foreach (var setting in settings) builder.UseSetting(setting.Key, setting.Value);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
            builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
            builder.ConfigureServices(services => services.ConfigureDbContext<EmployeeDbContext>((_, options) => options.AddInterceptors(gate)));
        }
    }
}

/// <summary>Describes a synthetic employee and optional address to seed into directory parity scenarios.</summary>
/// <param name="Id">The explicit employee identifier stored for ordering and exact-match assertions.</param>
/// <param name="FirstName">The literal first-name value stored for directory searches and responses.</param>
/// <param name="LastName">The literal last-name value stored for directory searches and full-name projection.</param>
/// <param name="Email">The synthetic email value stored in the employee profile.</param>
/// <param name="Phone">The optional phone value stored for directory search coverage.</param>
/// <param name="Address">Whether to create and attach a synthetic Thai home address to this employee.</param>
public sealed record DirectoryRow(int Id, string FirstName, string LastName, string Email, string? Phone = null, bool Address = false);

/// <summary>Pauses the next armed employee database read so tests can observe request cancellation.</summary>
public sealed class DirectoryCancellationGate : DbCommandInterceptor
{
    private int armed;
    /// <summary>Signals that an armed employee query has reached the cancellation wait.</summary>
    public TaskCompletionSource Entered { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    /// <summary>Signals that the waiting employee query observed cancellation.</summary>
    public TaskCompletionSource Cancelled { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Resets observation signals and arms a single upcoming employee read for cancellation.</summary>
    public void Arm()
    {
        Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Exchange(ref armed, 1);
    }

    /// <summary>Blocks the first armed employee reader command until cancellation and reports that cancellation before rethrowing.</summary>
    /// <param name="command">The pending database reader command inspected for an Employee table reference.</param>
    /// <param name="eventData">The Entity Framework execution metadata supplied for the pending reader command.</param>
    /// <param name="result">The interception result to preserve when the command is not cancelled.</param>
    /// <param name="cancellationToken">The request cancellation token used to interrupt the armed query wait.</param>
    /// <returns>The unchanged interception result for an unarmed command; the armed wait throws when its cancellation token is cancelled.</returns>
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("\"Employee\"", StringComparison.Ordinal) && Interlocked.Exchange(ref armed, 0) == 1)
        {
            Entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) { Cancelled.TrySetResult(); throw; }
        }
        return result;
    }
}

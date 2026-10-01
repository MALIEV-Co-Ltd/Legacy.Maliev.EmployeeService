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
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public DirectoryCancellationGate CancellationGate { get; } = new();

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        await redis.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
        Factory = new DirectoryFactory(postgres.GetConnectionString(),
            $"{redis.Hostname}:{redis.GetMappedPublicPort(6379)}", rsa, CancellationGate);
    }

    public async Task DisposeAsync()
    {
        if (Factory is not null) await Factory.DisposeAsync();
        rsa.Dispose();
        otherRsa.Dispose();
        await redis.DisposeAsync();
        await postgres.DisposeAsync();
    }

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

public sealed record DirectoryRow(int Id, string FirstName, string LastName, string Email, string? Phone = null, bool Address = false);

public sealed class DirectoryCancellationGate : DbCommandInterceptor
{
    private int armed;
    public TaskCompletionSource Entered { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Cancelled { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Arm()
    {
        Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Exchange(ref armed, 1);
    }

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

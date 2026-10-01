using System.Collections.Concurrent;
using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Legacy.Maliev.EmployeeService.Data;
using Maliev.Aspire.ServiceDefaults.IAM;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;

namespace Legacy.Maliev.EmployeeService.Tests;

// Normal Production JWT/permission middleware + real PG18/Redis. ONLY the remote
// IAM primary HTTP transport is controlled: this is not deployed IAM/grant proof.
public sealed class EmployeeRouteAcceptanceFixture : IAsyncLifetime
{
    private const string Issuer = "https://employee-route.example.invalid";
    private const string Audience = "employee-route-component";
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private readonly IContainer redis = new ContainerBuilder("redis:8-alpine")
        .WithPortBinding(6379, true).WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(6379)).Build();
    private readonly RSA key = RSA.Create(2048);
    private readonly RSA wrongKey = RSA.Create(2048);
    private readonly string liveCredential = Guid.NewGuid().ToString("N");
    public ConcurrentDictionary<string, RouteAuthority> Authorities { get; } = new();
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await postgres.StartAsync(); await redis.StartAsync();
        await using var db = CreateContext(); await db.Database.MigrateAsync();
        Factory = NewFactory(withIam: true);
    }

    public EmployeeDbContext CreateContext() => new(new DbContextOptionsBuilder<EmployeeDbContext>()
        .UseNpgsql(postgres.GetConnectionString()).Options);

    public WebApplicationFactory<Program> NewFactory(bool withIam) => new RouteFactory(this, withIam);
    public WebApplicationFactory<Program> DocumentationFactory() => new RouteFactory(this, false, "Testing");

    public WebApplicationFactory<Program> CacheRaceFactory(CachePublishSchedule schedule) => NewFactory(true)
        .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            var registration = services.Last(item => item.ServiceType == typeof(IDistributedCache));
            services.Remove(registration);
            services.AddSingleton<IDistributedCache>(provider =>
            {
                var actual = (IDistributedCache)(registration.ImplementationInstance
                    ?? registration.ImplementationFactory?.Invoke(provider)
                    ?? ActivatorUtilities.CreateInstance(provider, registration.ImplementationType!));
                return new ScheduledRedisCache(actual, schedule);
            });
        }));

    public WebApplicationFactory<Program> DatabaseRaceFactory(DatabaseReadSchedule schedule) => NewFactory(true)
        .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<EmployeeDbContext>(options => options.AddInterceptors(new ScheduledDatabaseReader(schedule)))));

    public HttpClient Client(string permission, string resource = "global", string authority = "valid",
        string decision = "allow", WebApplicationFactory<Program>? factory = null)
    {
        var client = (factory ?? Factory).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (authority == "anonymous") return client;
        var principal = "service:route-" + Guid.NewGuid().ToString("N");
        var expectation = new RouteAuthority(permission, resource, decision);
        Authorities[principal] = expectation;
        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, principal) };
        if (authority != "missing-permission") claims.Add(new("permissions", permission));
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(authority == "wrong-issuer" ? "https://wrong.example.invalid" : Issuer,
            authority == "wrong-audience" ? "wrong-audience" : Audience, claims,
            now.AddHours(-1), authority == "expired" ? now.AddMinutes(-10) : now.AddMinutes(5),
            new SigningCredentials(new RsaSecurityKey(authority == "wrong-signature" ? wrongKey : key), SecurityAlgorithms.RsaSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        return client;
    }

    public async Task DisposeAsync()
    {
        if (Factory is not null) await Factory.DisposeAsync();
        key.Dispose(); wrongKey.Dispose(); await redis.DisposeAsync(); await postgres.DisposeAsync();
    }

    private sealed class RouteFactory(EmployeeRouteAcceptanceFixture fixture, bool withIam, string environment = "Production") : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:EmployeeDbContext"] = fixture.postgres.GetConnectionString(),
                ["ConnectionStrings:redis"] = $"{fixture.redis.Hostname}:{fixture.redis.GetMappedPublicPort(6379)}",
                ["Cache:RedisEnabled"] = "true",
                ["Jwt:PublicKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(fixture.key.ExportSubjectPublicKeyInfoPem())),
                ["Jwt:Issuer"] = Issuer,
                ["Jwt:Audience"] = Audience,
                ["Logging:LogLevel:Default"] = "Warning",
                ["Features:ResourceScopedAuthEnabled"] = "true",
                ["IAM:LivePermissionChecks:Credential"] = fixture.liveCredential,
            };
            foreach (var item in settings) builder.UseSetting(item.Key, item.Value);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
            builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
            if (withIam) builder.ConfigureServices(services =>
            {
                services.AddScoped<IIamServiceClient, IamServiceClient>();
                services.AddHttpClient("IAMService", client => client.BaseAddress = new Uri("https://controlled-iam.example.invalid"))
                    .ConfigurePrimaryHttpMessageHandler(() => new StrictIamTransport(fixture));
            });
        }
    }

    private sealed class StrictIamTransport(EmployeeRouteAcceptanceFixture fixture) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/iam/v1/auth/check-permission", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var json = body.RootElement;
            var principal = json.GetProperty("principalId").GetString()!;
            Assert.True(fixture.Authorities.TryGetValue(principal, out var expected));
            Assert.Equal(expected!.Permission, json.GetProperty("permissionId").GetString());
            Assert.Equal(expected.Resource, json.GetProperty("resourcePath").GetString());
            var live = json.GetProperty("bypassCache").GetBoolean();
            Interlocked.Increment(ref expected.Calls);
            if (live)
            {
                Interlocked.Increment(ref expected.LiveCalls);
                Assert.True(request.Headers.TryGetValues("X-Maliev-IAM-Live-Check-Key", out var header)
                    && header.Single() == fixture.liveCredential);
            }
            else Assert.False(request.Headers.Contains("X-Maliev-IAM-Live-Check-Key"));
            // Standard checks deliberately deny upstream and exercise signed exact claim
            // fallback. Forced-live checks exercise the actual client's response parsing.
            if (live && expected.Decision == "unavailable") return new(HttpStatusCode.ServiceUnavailable);
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(live && expected.Decision == "malformed" ? "not-json"
                    : live && expected.Decision == "allow" ? "{\"allowed\":true}" : "{\"allowed\":false}", Encoding.UTF8, "application/json"),
            };
        }
    }
}

public sealed class DatabaseReadSchedule
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Reads;
}

internal sealed class ScheduledDatabaseReader(DatabaseReadSchedule schedule) : DbCommandInterceptor
{
    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
        DbDataReader result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("FROM \"Employee\"", StringComparison.Ordinal)
            && Interlocked.Increment(ref schedule.Reads) == 1)
        {
            schedule.Entered.TrySetResult();
            await schedule.Release.Task.WaitAsync(cancellationToken);
        }
        return result;
    }
}

// Scheduling only; every cache operation still reaches the actual Redis adapter.
public sealed class CachePublishSchedule(string key)
{
    public string Key { get; } = key;
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Publications;
    public bool DenyReads { get; init; }
    public bool DenyRemovals { get; init; }
    public int ReadAttempts;
    public int RemoveAttempts;
}

internal sealed class ScheduledRedisCache(IDistributedCache actual, CachePublishSchedule schedule) : IDistributedCache
{
    public byte[]? Get(string key) => actual.Get(key);
    public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        Interlocked.Increment(ref schedule.ReadAttempts);
        return schedule.DenyReads ? Task.FromException<byte[]?>(new InvalidOperationException("Controlled cache read denial")) : actual.GetAsync(key, token);
    }
    public void Refresh(string key) => actual.Refresh(key);
    public Task RefreshAsync(string key, CancellationToken token = default) => actual.RefreshAsync(key, token);
    public void Remove(string key) => actual.Remove(key);
    public Task RemoveAsync(string key, CancellationToken token = default)
    {
        Interlocked.Increment(ref schedule.RemoveAttempts);
        return schedule.DenyRemovals ? Task.FromException(new InvalidOperationException("Controlled cache removal denial")) : actual.RemoveAsync(key, token);
    }
    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => actual.Set(key, value, options);
    public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        if (key == schedule.Key && Interlocked.Increment(ref schedule.Publications) == 1)
        {
            schedule.Entered.TrySetResult();
            await schedule.Release.Task.WaitAsync(token);
        }
        await actual.SetAsync(key, value, options, token);
    }
}

public sealed class RouteAuthority(string permission, string resource, string decision)
{
    public string Permission { get; } = permission;
    public string Resource { get; } = resource;
    public string Decision { get; } = decision;
    public int Calls;
    public int LiveCalls;
}

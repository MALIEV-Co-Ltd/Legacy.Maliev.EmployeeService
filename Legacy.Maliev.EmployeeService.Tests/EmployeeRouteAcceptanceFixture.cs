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
/// <summary>Owns PostgreSQL, Redis, signing keys, and HTTP hosts that exercise production JWT and permission middleware with only the remote IAM transport controlled.</summary>
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
    /// <summary>The synthetic principal-to-authority expectations checked by the controlled IAM HTTP transport.</summary>
    public ConcurrentDictionary<string, RouteAuthority> Authorities { get; } = new();
    internal IamTransportContractEvidence IamTransportEvidence { get; } = new();
    /// <summary>The production test host configured with fixture database, Redis, JWT settings, and the controlled IAM transport.</summary>
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    /// <summary>Starts fixture containers, migrates the employee database, and prepares the production route host.</summary>
    /// <returns>A task that completes when PostgreSQL, Redis, migrations, and the default host factory are ready.</returns>
    public async Task InitializeAsync()
    {
        await postgres.StartAsync(); await redis.StartAsync();
        await using var db = CreateContext(); await db.Database.MigrateAsync();
        Factory = NewFactory(withIam: true);
    }

    /// <summary>Creates an independently disposable context connected to the fixture PostgreSQL database.</summary>
    /// <returns>A caller-owned employee database context using the fixture connection.</returns>
    public EmployeeDbContext CreateContext() => new(new DbContextOptionsBuilder<EmployeeDbContext>()
        .UseNpgsql(postgres.GetConnectionString()).Options);

    /// <summary>Creates a production host factory with optional controlled remote IAM transport registration.</summary>
    /// <param name="withIam">Whether to register the real IAM client with the fixture-controlled primary HTTP transport.</param>
    /// <returns>A caller-owned production host factory sharing the fixture database, cache, and signing settings.</returns>
    public WebApplicationFactory<Program> NewFactory(bool withIam) => new RouteFactory(this, withIam);
    /// <summary>Creates a Testing host factory for documentation checks without the controlled IAM client registration.</summary>
    /// <returns>A caller-owned documentation host factory in the Testing environment.</returns>
    public WebApplicationFactory<Program> DocumentationFactory() => new RouteFactory(this, false, "Testing");

    /// <summary>Wraps the actual distributed cache with scheduling controls for first-publication races and optional cache failures.</summary>
    /// <param name="schedule">The cache key, publication signals, and read or removal failure controls applied around actual Redis operations.</param>
    /// <returns>A caller-owned production host factory using the scheduled Redis wrapper.</returns>
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

    /// <summary>Adds a reader interceptor that pauses the first employee database result for a controlled read race.</summary>
    /// <param name="schedule">The first-read entry and release signals and read counter shared with the database interceptor.</param>
    /// <returns>A caller-owned production host factory containing the scheduled database reader interceptor.</returns>
    public WebApplicationFactory<Program> DatabaseRaceFactory(DatabaseReadSchedule schedule) => NewFactory(true)
        .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<EmployeeDbContext>(options => options.AddInterceptors(new ScheduledDatabaseReader(schedule)))));

    /// <summary>Creates an HTTP client with a synthetic signed identity and matching controlled IAM expectations for permission checks.</summary>
    /// <param name="permission">The primary permission identifier expected in the IAM request and normally included in the signed token.</param>
    /// <param name="resource">The resource path expected by the controlled IAM transport for the primary permission.</param>
    /// <param name="authority">The token scenario, including anonymous, missing-permission, expired, wrong-issuer, wrong-audience, or wrong-signature.</param>
    /// <param name="decision">The controlled forced-live IAM outcome: allow, deny, unavailable, or malformed response.</param>
    /// <param name="factory">An optional alternate host factory; null uses the fixture default host.</param>
    /// <param name="additionalPermissions">Optional additional permission identifiers mapped to their expected resource paths for multi-permission actions.</param>
    /// <param name="permissionClaims">Optional explicit signed permission claims overriding the default primary and additional permission roster.</param>
    /// <returns>A caller-owned HTTP client targeting the selected host with the requested token and registered IAM expectations.</returns>
    public HttpClient Client(string permission, string resource = "global", string authority = "valid",
        string decision = "allow", WebApplicationFactory<Program>? factory = null,
        IReadOnlyDictionary<string, string>? additionalPermissions = null, string[]? permissionClaims = null)
    {
        var client = (factory ?? Factory).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (authority == "anonymous") return client;
        var principal = "service:route-" + Guid.NewGuid().ToString("N");
        // An authority-refusal scenario must also be denied by the authoritative
        // live IAM response; a missing cached JWT permission is not a live denial.
        var expectation = new RouteAuthority(permission, resource, authority == "missing-permission" ? "deny" : decision)
        { AdditionalResources = additionalPermissions };
        Authorities[principal] = expectation;
        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, principal) };
        if (authority != "missing-permission")
            claims.AddRange((permissionClaims ?? [permission, .. (additionalPermissions?.Keys ?? [])])
                .Select(value => new Claim("permissions", value)));
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(authority == "wrong-issuer" ? "https://wrong.example.invalid" : Issuer,
            authority == "wrong-audience" ? "wrong-audience" : Audience, claims,
            now.AddHours(-1), authority == "expired" ? now.AddMinutes(-10) : now.AddMinutes(5),
            new SigningCredentials(new RsaSecurityKey(authority == "wrong-signature" ? wrongKey : key), SecurityAlgorithms.RsaSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        return client;
    }

    /// <summary>Releases the fixture host, signing keys, and containers, then verifies controlled IAM transport evidence.</summary>
    /// <returns>A task that completes after owned resources are disposed and IAM transport contract assertions succeed.</returns>
    public async Task DisposeAsync()
    {
        if (Factory is not null) await Factory.DisposeAsync();
        key.Dispose(); wrongKey.Dispose(); await redis.DisposeAsync(); await postgres.DisposeAsync();
        IamTransportEvidence.AssertHealthy();
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
            => await fixture.IamTransportEvidence.ObserveAsync(() => SendCheckedAsync(request, token), token);

        private async Task<HttpResponseMessage> SendCheckedAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/iam/v1/auth/check-permission", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var json = body.RootElement;
            Assert.Equal(new[] { "bypassCache", "permissionId", "principalId", "resourcePath" },
                json.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
            var principal = json.GetProperty("principalId").GetString()!;
            Assert.True(fixture.Authorities.TryGetValue(principal, out var expected));
            var permission = json.GetProperty("permissionId").GetString()!;
            Assert.NotNull(expected);
            var resource = expected.AdditionalResources is not null && expected.AdditionalResources.TryGetValue(permission, out var additionalResource)
                ? additionalResource : expected.Resource;
            if (expected.AdditionalResources is null || !expected.AdditionalResources.ContainsKey(permission))
                Assert.Equal(expected.Permission, permission);
            Assert.Equal(resource, json.GetProperty("resourcePath").GetString());
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
                    : JsonSerializer.Serialize(new
                    {
                        principalId = expected.ResolvedPrincipalId,
                        permissionId = permission,
                        resourcePath = resource,
                        allowed = live && expected.Decision == "allow",
                        fromCache = false,
                        latencyMs = 0
                    }), Encoding.UTF8, "application/json"),
            };
        }
    }
}

/// <summary>Coordinates a pause after the first employee database read so tests can control competing operations.</summary>
public sealed class DatabaseReadSchedule
{
    /// <summary>Signals that the first employee database reader has produced a result and reached the scheduled pause.</summary>
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    /// <summary>Allows the paused employee database result to continue to its caller.</summary>
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    /// <summary>Counts employee reader results observed by the scheduled interceptor.</summary>
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
/// <summary>Coordinates first publication of one employee cache key while preserving actual Redis operations.</summary>
/// <param name="key">The exact distributed cache key whose first asynchronous publication is paused.</param>
public sealed class CachePublishSchedule(string key)
{
    /// <summary>The exact cache key selected for the first-publication pause.</summary>
    public string Key { get; } = key;
    /// <summary>Signals that the selected key reached its first asynchronous cache publication.</summary>
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    /// <summary>Releases the selected key publication so its actual Redis write can proceed.</summary>
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    /// <summary>Counts matching-key asynchronous publication attempts observed by the wrapper.</summary>
    public int Publications;
    /// <summary>Controls whether asynchronous cache reads fail before reaching Redis.</summary>
    public bool DenyReads { get; init; }
    /// <summary>Controls whether asynchronous cache removals fail before reaching Redis.</summary>
    public bool DenyRemovals { get; init; }
    /// <summary>Counts asynchronous cache read attempts observed by the wrapper.</summary>
    public int ReadAttempts;
    /// <summary>Counts asynchronous cache removal attempts observed by the wrapper.</summary>
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

/// <summary>Describes expected permission and resource checks and the controlled live IAM response for one synthetic principal.</summary>
/// <param name="permission">The primary permission identifier expected in controlled IAM requests.</param>
/// <param name="resource">The expected resource path for the primary permission.</param>
/// <param name="decision">The controlled forced-live IAM outcome selecting allow, denial, unavailable service, or malformed response.</param>
public sealed class RouteAuthority(string permission, string resource, string decision)
{
    /// <summary>Optional expected resource paths keyed by additional permission identifiers for actions requiring several permissions.</summary>
    public IReadOnlyDictionary<string, string>? AdditionalResources { get; init; }
    /// <summary>The synthetic resolved principal identifier returned in controlled IAM response payloads.</summary>
    public Guid ResolvedPrincipalId { get; } = Guid.NewGuid();
    /// <summary>The primary permission identifier expected for this synthetic principal.</summary>
    public string Permission { get; } = permission;
    /// <summary>The expected resource path for the primary permission check.</summary>
    public string Resource { get; } = resource;
    /// <summary>The selected controlled forced-live response outcome.</summary>
    public string Decision { get; } = decision;
    /// <summary>Counts all controlled IAM HTTP permission requests observed for this principal.</summary>
    public int Calls;
    /// <summary>Counts controlled IAM permission requests that explicitly bypass the cache.</summary>
    public int LiveCalls;
}

using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.EmployeeService.Api.Logging;
using Legacy.Maliev.EmployeeService.Application.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace Legacy.Maliev.EmployeeService.Tests;

/// <summary>Serializes production pipeline tests that inspect host-owned diagnostics and shared employee state.</summary>
[CollectionDefinition("Employee production pipeline", DisableParallelization = true)]
public sealed class EmployeeProductionPipelineHttpTestsCollection;

/// <summary>Verifies production CORS, health responses, private diagnostics, and ordinary failure handling through HTTP.</summary>
/// <param name="fixture">The shared Employee route fixture supplying the production application factory, PostgreSQL contexts, Redis, and controlled IAM request authority.</param>
[Collection("Employee production pipeline")]
public sealed class EmployeeProductionPipelineHttpTests(EmployeeRouteAcceptanceFixture fixture)
    : IClassFixture<EmployeeRouteAcceptanceFixture>
{
    private const string AllowedOrigin = "https://example.test";

    /// <summary>Checks production preflight responses before mutation authorization and exposes CORS headers only for configured origins.</summary>
    /// <param name="allowed">Whether the request Origin matches the configured allowed origin rather than a blocked external origin.</param>
    /// <returns>A task that completes after verifying production preflight responses before mutation authorization and exposes CORS headers only for configured origins.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProductionPreflight_ContainsConfiguredOriginBeforeProtectedMutationAuthorization(bool allowed)
    {
        await using var host = NewHost();
        Assert.Equal("Production", host.Services.GetRequiredService<IWebHostEnvironment>().EnvironmentName);
        Assert.Contains(AllowedOrigin, host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions>>()
            .Value.GetPolicy("__DefaultCorsPolicy")!.Origins);
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Options, "/employees/17/");
        request.Headers.Add("Origin", allowed ? AllowedOrigin : "https://blocked.example.invalid");
        request.Headers.Add("Access-Control-Request-Method", "PUT");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        if (allowed)
        {
            Assert.Equal(AllowedOrigin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
            Assert.Equal("true", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Credentials")));
            Assert.Contains("PUT", response.Headers.GetValues("Access-Control-Allow-Methods"));
            var headers = string.Join(",", response.Headers.GetValues("Access-Control-Allow-Headers"));
            Assert.Contains("authorization", headers, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("content-type", headers, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
            Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
        }
    }

    /// <summary>Checks that anonymous mutation returns 401 with configured-origin CORS headers for browser error handling.</summary>
    /// <returns>A task that completes after verifying that anonymous mutation returns 401 with configured-origin CORS headers for browser error handling.</returns>
    [Fact]
    public async Task ProductionAnonymousMutation_ReturnsUnauthorizedWithConfiguredOriginForBrowserErrorHandling()
    {
        await using var host = NewHost();
        Assert.Contains(AllowedOrigin, host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions>>()
            .Value.GetPolicy("__DefaultCorsPolicy")!.Origins);
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Put, "/employees/17/")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Origin", AllowedOrigin);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(AllowedOrigin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Equal("true", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Credentials")));
    }

    /// <summary>Checks anonymous health responses and omission of private readiness detail fields.</summary>
    /// <param name="probe">The liveness or readiness health endpoint under /employee.</param>
    /// <returns>A task that completes after verifying anonymous health responses and omission of private readiness detail fields.</returns>
    [Theory]
    [InlineData("liveness")]
    [InlineData("readiness")]
    public async Task ProductionHealthyProbe_RemainsAnonymousAndUsesApprovedSanitizedWireShape(string probe)
    {
        await using var host = NewHost();
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.GetAsync($"/employee/{probe}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        if (probe == "liveness")
        {
            Assert.Equal("Healthy", body);
            return;
        }
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("Healthy", json.RootElement.GetProperty("status").GetString());
        Assert.True(json.RootElement.GetProperty("totalDuration").GetDouble() >= 0);
        Assert.NotEmpty(json.RootElement.GetProperty("checks").EnumerateObject());
        foreach (var check in json.RootElement.GetProperty("checks").EnumerateObject())
        {
            Assert.Equal("Healthy", check.Value.GetProperty("status").GetString());
            Assert.True(check.Value.GetProperty("duration").GetDouble() >= 0);
            foreach (var privateField in new[] { "description", "data", "exception" })
                Assert.False(check.Value.TryGetProperty(privateField, out _));
        }
    }

    private const string DiagnosticPath = "/internal/diagnostics/observability";
    private const string ObservationCategory = "Maliev.Aspire.ServiceDefaults.Diagnostics.PrivateRequestObservation";
    private const string FrameworkCategory = "Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware";

    /// <summary>Checks quiet uncached health identity that stays stable per host and differs between independent hosts.</summary>
    /// <param name="probe">The liveness or readiness endpoint used to compare repeated and cross-host health identifiers.</param>
    /// <returns>A task that completes after verifying quiet uncached health identity that stays stable per host and differs between independent hosts.</returns>
    [Theory]
    [InlineData("liveness")]
    [InlineData("readiness")]
    public async Task ProductionHealthIdentity_IsQuietUncachedStableAndHostOwned(string probe)
    {
        using var logs = new ProbeLogProvider();
        await using var host = NewProbeHost(logs);
        InitializeLogs(host, logs);
        var before = await SnapshotDomainAsync();
        using var client = host.CreateClient();
        Assert.Null(client.DefaultRequestHeaders.Authorization);
        using var first = await client.GetAsync($"/employee/{probe}");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var marker = HealthMarker(first);
        using var repeated = await client.GetAsync($"/employee/{probe}");
        Assert.Equal(marker, HealthMarker(repeated));
        using var otherProbe = await client.GetAsync(probe == "readiness" ? "/employee/liveness" : "/employee/readiness");
        Assert.Equal(marker, HealthMarker(otherProbe));
        Assert.DoesNotContain(logs.Events, entry => entry.Category == ObservationCategory);
        Assert.DoesNotContain(logs.Events, entry => entry.Category == FrameworkCategory && entry.Level >= LogLevel.Error);

        using var independentLogs = new ProbeLogProvider();
        await using var independent = NewProbeHost(independentLogs);
        InitializeLogs(independent, independentLogs);
        using var independentClient = independent.CreateClient();
        using var separate = await independentClient.GetAsync($"/employee/{probe}");
        Assert.Equal(HttpStatusCode.OK, separate.StatusCode);
        Assert.NotEqual(marker, HealthMarker(separate));
        Assert.Equal(before, await SnapshotDomainAsync());
        Assert.False(logs.Overflowed);
        Assert.False(independentLogs.Overflowed);
    }

    /// <summary>Checks that non-GET and unregistered health-shaped routes expose no diagnostic identity markers.</summary>
    /// <param name="method">The HTTP verb sent to the registered or health-shaped test route.</param>
    /// <param name="path">The route used to test non-GET or unregistered health matching.</param>
    /// <param name="status">The expected numeric HTTP status, including 200, 404, or 405, for the selected route and verb.</param>
    /// <returns>A task that completes after verifying that non-GET and unregistered health-shaped routes expose no diagnostic identity markers.</returns>
    [Theory]
    [InlineData("POST", "/employee/liveness", 405)]
    [InlineData("POST", "/employee/readiness", 200)]
    [InlineData("GET", "/employee/aspire-liveness", 200)]
    [InlineData("GET", "/employees/private/readiness", 404)]
    [InlineData("GET", "/employee/readiness/extra", 404)]
    public async Task ProductionHealthIdentity_ExcludesNonGetAndUnregisteredRoutes(string method, string path, int status)
    {
        using var logs = new ProbeLogProvider();
        await using var host = NewProbeHost(logs);
        InitializeLogs(host, logs);
        var before = await SnapshotDomainAsync();
        using var client = host.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        using var response = await client.SendAsync(request);
        Assert.Equal(status, (int)response.StatusCode);
        Assert.False(response.Headers.Contains("X-Maliev-Health-Instance"));
        Assert.False(response.Headers.Contains("X-Maliev-Diagnostic-Id"));
        Assert.DoesNotContain(logs.Events, entry => entry.EventName == "ObservabilityPipelineProbe");
        Assert.Equal(before, await SnapshotDomainAsync());
        Assert.False(logs.Overflowed);
    }

    /// <summary>Checks that diagnostic denial exposes no markers and leaves the host budget available for an admitted probe.</summary>
    /// <param name="peer">The direct remote IP address, or null, supplied to the diagnostic request.</param>
    /// <param name="method">The GET or rejected POST verb used for diagnostic admission.</param>
    /// <param name="nonceKind">The nonce representation: valid compact GUID, invalid text, dashed GUID, or multiple header values.</param>
    /// <param name="spoofLoopback">Whether to supply forwarded loopback headers while the direct peer remains nonloopback or absent.</param>
    /// <returns>A task that completes after verifying that diagnostic denial exposes no markers and leaves the host budget available for an admitted probe.</returns>
    [Theory]
    [InlineData("203.0.113.9", "GET", "valid", true)]
    [InlineData(null, "GET", "valid", true)]
    [InlineData("127.0.0.1", "POST", "valid", false)]
    [InlineData("127.0.0.1", "GET", "invalid", false)]
    [InlineData("127.0.0.1", "GET", "dashed", false)]
    [InlineData("127.0.0.1", "GET", "multiple", false)]
    public async Task ProductionDiagnostic_DenialIsMarkerFreeAndDoesNotConsumeTheHostBudget(
        string? peer, string method, string nonceKind, bool spoofLoopback)
    {
        using var logs = new ProbeLogProvider();
        await using var host = NewProbeHost(logs);
        InitializeLogs(host, logs);
        var before = await SnapshotDomainAsync();
        var nonce = Guid.NewGuid();
        string[] values = nonceKind switch
        {
            "invalid" => ["not-a-guid"],
            "dashed" => [nonce.ToString("D")],
            "multiple" => [nonce.ToString("N"), nonce.ToString("N")],
            _ => [nonce.ToString("N")],
        };
        var denied = await SendDiagnosticAsync(host, method, peer, values, spoofLoopback);
        Assert.Equal(404, denied.Response.StatusCode);
        Assert.Equal("no-store", denied.Response.Headers.CacheControl.ToString());
        Assert.Equal("noindex", denied.Response.Headers["X-Robots-Tag"].ToString());
        Assert.False(denied.Response.Headers.ContainsKey("X-Maliev-Health-Instance"));
        Assert.False(denied.Response.Headers.ContainsKey("X-Maliev-Diagnostic-Id"));
        Assert.DoesNotContain(logs.Events, entry => entry.Category == ObservationCategory);
        Assert.DoesNotContain(logs.Events, entry => entry.Category == FrameworkCategory && entry.Level >= LogLevel.Error);

        var accepted = await SendDiagnosticAsync(host, "GET", "127.0.0.1", [nonce.ToString("N")]);
        AssertSynthetic(accepted, nonce.ToString("N"));
        Assert.Equal(new[] { LogLevel.Warning, LogLevel.Error, LogLevel.Critical },
            logs.Events.Where(entry => entry.Category == ObservationCategory || entry.Category == FrameworkCategory)
                .Select(entry => entry.Level).ToArray());
        Assert.Equal(before, await SnapshotDomainAsync());
        Assert.False(logs.Overflowed);
    }

    /// <summary>Checks compact nonce canonicalization and reopening of the host diagnostic budget at exactly one minute.</summary>
    /// <returns>A task that completes after verifying compact nonce canonicalization and reopening of the host diagnostic budget at exactly one minute.</returns>
    [Fact]
    public async Task ProductionDiagnostic_CanonicalizesNonceAndReopensOnlyAtTheExactMinute()
    {
        using var logs = new ProbeLogProvider();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero));
        await using var host = NewProbeHost(logs, clock);
        InitializeLogs(host, logs);
        var before = await SnapshotDomainAsync();
        const string uppercaseNonce = "ABCDEF0123456789ABCDEF0123456789";
        var accepted = await SendDiagnosticAsync(host, "GET", "::1", [uppercaseNonce]);
        AssertSynthetic(accepted, uppercaseNonce.ToLowerInvariant());
        var marker = accepted.Response.Headers["X-Maliev-Health-Instance"].ToString();
        var throttled = await SendDiagnosticAsync(host, "GET", "::1", [Guid.Empty.ToString("N")]);
        AssertThrottled(throttled);
        clock.Advance(TimeSpan.FromMilliseconds(59_999));
        var early = await SendDiagnosticAsync(host, "GET", "::1", [Guid.Empty.ToString("N")]);
        AssertThrottled(early);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        var reopened = await SendDiagnosticAsync(host, "GET", "::1", [Guid.Empty.ToString("N")]);
        AssertSynthetic(reopened, Guid.Empty.ToString("N"));
        Assert.Equal(marker, reopened.Response.Headers["X-Maliev-Health-Instance"].ToString());
        using var client = host.CreateClient();
        using var health = await client.GetAsync("/employee/liveness");
        Assert.Equal(marker, HealthMarker(health));
        Assert.Equal(2, logs.Events.Count(entry => entry.EventName == "ObservabilityPipelineProbe"));
        Assert.Equal(2, logs.Events.Count(entry => entry.Category == FrameworkCategory && entry.Level == LogLevel.Error));
        Assert.Equal(2, logs.Events.Count(entry => entry.EventName == "UnhandledRequestFailure" && entry.Category == ObservationCategory));
        Assert.Equal(before, await SnapshotDomainAsync());
        Assert.False(logs.Overflowed);
    }

    /// <summary>Checks a private ordinary 500 response and one incident without diagnostic-probe headers.</summary>
    /// <returns>A task that completes after verifying a private ordinary 500 response and one incident without diagnostic-probe headers.</returns>
    [Fact]
    public async Task ProductionOrdinaryThrownFailure_PreservesPrivateJsonAndSingleIncidentWithoutProbeHeaders()
    {
        const string privateFailure = "synthetic-employee-private-failure";
        var service = new Mock<IEmployeeService>(MockBehavior.Strict);
        service.Setup(value => value.GetEmployeeAsync(17, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException(privateFailure));
        using var logs = new ProbeLogProvider();
        await using var host = NewProbeHost(logs, service: service.Object);
        InitializeLogs(host, logs);
        var before = await SnapshotDomainAsync();
        using var client = fixture.Client("legacy-employee.employees.read", "/employees/17", factory: host);
        using var response = await client.GetAsync("/employees/17");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal("An internal server error occurred", json.RootElement.GetProperty("error").GetString());
        Assert.Equal(500, json.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("details").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("traceId").GetString()));
        Assert.DoesNotContain(privateFailure, body, StringComparison.Ordinal);
        Assert.False(response.Headers.Contains("X-Maliev-Health-Instance"));
        Assert.False(response.Headers.Contains("X-Maliev-Diagnostic-Id"));
        Assert.Single(logs.Events, entry => entry.EventName == "UnhandledRequestFailure" && entry.Level == LogLevel.Critical);
        Assert.DoesNotContain(logs.Events, entry => entry.Category == ObservationCategory);
        Assert.All(logs.Events, entry => Assert.DoesNotContain(privateFailure, entry.ConsoleJson, StringComparison.Ordinal));
        service.Verify(value => value.GetEmployeeAsync(17, It.IsAny<CancellationToken>()), Times.Once);
        service.VerifyNoOtherCalls();
        Assert.Equal(before, await SnapshotDomainAsync());
        Assert.False(logs.Overflowed);
    }

    private WebApplicationFactory<Program> NewProbeHost(ProbeLogProvider logs, TimeProvider? clock = null,
        IEmployeeService? service = null) => fixture.Factory.WithWebHostBuilder(builder =>
    {
        builder.UseSetting("CORS:AllowedOrigins:0", AllowedOrigin);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["CORS:AllowedOrigins:0"] = AllowedOrigin }));
        builder.ConfigureLogging(logging => logging.AddProvider(logs));
        builder.ConfigureTestServices(services =>
        {
            if (clock is not null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(clock);
            }
            if (service is not null)
            {
                services.RemoveAll<IEmployeeService>();
                services.AddScoped<IEmployeeService>(_ => service);
            }
        });
    });

    private static void InitializeLogs(WebApplicationFactory<Program> host, ProbeLogProvider logs)
    {
        Assert.Equal("Production", host.Services.GetRequiredService<IWebHostEnvironment>().EnvironmentName);
        Assert.Equal(EmployeeDiagnosticConsoleFormatter.FormatterName,
            host.Services.GetRequiredService<IOptions<ConsoleLoggerOptions>>().Value.FormatterName);
        logs.Formatter = Assert.Single(host.Services.GetServices<ConsoleFormatter>(),
            formatter => formatter.Name == EmployeeDiagnosticConsoleFormatter.FormatterName);
        logs.Clear();
    }

    private static string HealthMarker(HttpResponseMessage response)
    {
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.True(response.Headers.TryGetValues("X-Maliev-Health-Instance", out var markers));
        Assert.NotNull(markers);
        var value = Assert.Single(markers);
        Assert.Matches("^[a-f0-9]{32}$", value);
        return value;
    }

    private static async Task<HttpContext> SendDiagnosticAsync(WebApplicationFactory<Program> host, string method,
        string? peer, string[] nonces, bool spoofLoopback = false)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var context = await host.Server.SendAsync(request =>
        {
            request.Request.Method = method;
            request.Request.Scheme = "https";
            request.Request.Path = DiagnosticPath;
            request.Connection.RemoteIpAddress = peer is null ? null : IPAddress.Parse(peer);
            request.Request.Headers["X-Maliev-Diagnostic-Id"] = new StringValues(nonces);
            if (spoofLoopback)
            {
                request.Request.Headers["X-Forwarded-For"] = "127.0.0.1";
                request.Request.Headers["X-Real-IP"] = "127.0.0.1";
            }
        }, deadline.Token);
        // TestServer returns at response start. Await real EOF before reading terminal events.
        Assert.Equal(0, await context.Response.Body.ReadAsync(new byte[1], deadline.Token));
        return context;
    }

    private static void AssertSynthetic(HttpContext context, string nonce)
    {
        Assert.Equal(500, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Equal(nonce, context.Response.Headers["X-Maliev-Diagnostic-Id"].ToString());
        Assert.Matches("^[a-f0-9]{32}$", context.Response.Headers["X-Maliev-Health-Instance"].ToString());
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal("noindex", context.Response.Headers["X-Robots-Tag"].ToString());
    }

    private static void AssertThrottled(HttpContext context)
    {
        Assert.Equal(429, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.False(context.Response.Headers.ContainsKey("X-Maliev-Health-Instance"));
        Assert.False(context.Response.Headers.ContainsKey("X-Maliev-Diagnostic-Id"));
    }

    private async Task<string> SnapshotDomainAsync()
    {
        await using var database = fixture.CreateContext();
        var connection = database.Database.GetDbConnection();
        await connection.OpenAsync();
        var result = new StringBuilder();
        foreach (var table in new[] { "Address", "Role", "Employee", "SignatureImageFile" })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COALESCE(jsonb_agg(row_value ORDER BY row_value->>'ID'), '[]'::jsonb)::text FROM (SELECT to_jsonb(t) || jsonb_build_object('__xmin', t.xmin::text) AS row_value FROM \"{table}\" t) snapshot";
            result.Append(table).Append(':').Append(await command.ExecuteScalarAsync()).AppendLine();
        }
        return result.ToString();
    }

    private sealed record ProbeEvent(string Category, LogLevel Level, string? EventName, string ConsoleJson);

    private sealed class ProbeLogProvider : ILoggerProvider, ISupportExternalScope
    {
        private readonly object gate = new();
        private IExternalScopeProvider scopes = new LoggerExternalScopeProvider();
        private int count;
        private int overflowed;
        public ConcurrentQueue<ProbeEvent> Events { get; } = new();
        public ConsoleFormatter? Formatter { get; set; }
        public bool Overflowed => Volatile.Read(ref overflowed) != 0;

        // Called only after startup and before request traffic.
        public void Clear()
        {
            lock (gate)
            {
                Events.Clear();
                Interlocked.Exchange(ref count, 0);
                Interlocked.Exchange(ref overflowed, 0);
            }
        }
        public ILogger CreateLogger(string categoryName) => new ProbeLogger(this, categoryName);
        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => scopes = scopeProvider;
        public void Dispose() { }

        private sealed class ProbeLogger(ProbeLogProvider owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => owner.scopes.Push(state);
            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning && logLevel != LogLevel.None;
            public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(level)) return;
                lock (owner.gate)
                {
                    if (owner.count == 64)
                    {
                        Interlocked.Exchange(ref owner.overflowed, 1);
                        return;
                    }
                    Interlocked.Increment(ref owner.count);
                    var eventName = state is IEnumerable<KeyValuePair<string, object?>> fields
                        ? fields.FirstOrDefault(field => field.Key == "EventName").Value as string
                        : null;
                    using var output = new StringWriter();
                    if (owner.Formatter is { } selected)
                    {
                        var entry = new LogEntry<TState>(level, category, eventId, state, exception, formatter);
                        selected.Write(in entry, owner.scopes, output);
                    }
                    owner.Events.Enqueue(new ProbeEvent(category, level, eventName, output.ToString()));
                }
            }
        }
    }

    private WebApplicationFactory<Program> NewHost() => fixture.Factory.WithWebHostBuilder(builder =>
    {
        builder.UseSetting("CORS:AllowedOrigins:0", AllowedOrigin);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["CORS:AllowedOrigins:0"] = AllowedOrigin }));
    });
}

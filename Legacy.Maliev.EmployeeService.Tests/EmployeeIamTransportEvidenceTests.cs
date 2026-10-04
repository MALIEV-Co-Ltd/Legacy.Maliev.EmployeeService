using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Maliev.Aspire.ServiceDefaults.IAM;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Sdk;

namespace Legacy.Maliev.EmployeeService.Tests;

public sealed class EmployeeIamTransportEvidenceTests
{
    [Fact]
    public async Task RealIamClient_CatchesTransportAssertion_ButExternalEvidenceStillFails()
    {
        var evidence = new IamTransportContractEvidence();
        using var transport = new ObservedTransport(evidence, request =>
        {
            Assert.Equal("/deliberately-wrong-test-route", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var http = new HttpClient(transport) { BaseAddress = new Uri("https://controlled-iam.example.invalid") };
        var client = CreateClient(http);

        var allowed = await client.CheckPermissionAsync("service:evidence-" + Guid.NewGuid().ToString("N"),
            "legacy-employee.employees.read", "/employees/17");

        Assert.False(allowed);
        Assert.Equal(1, evidence.FailureCount);
        Assert.ThrowsAny<XunitException>(() => evidence.AssertHealthy());
    }

    [Theory]
    [InlineData("standard-deny", false)]
    [InlineData("live-allow", true)]
    [InlineData("live-unavailable", false)]
    [InlineData("live-malformed", false)]
    public async Task RealIamClient_ValidRequestWireAndIntentionalRemoteOutcomes_DoNotBecomeFixtureFaults(
        string outcome, bool expected)
    {
        var evidence = new IamTransportContractEvidence();
        var principal = "service:evidence-" + Guid.NewGuid().ToString("N");
        var resolvedPrincipal = Guid.NewGuid();
        const string permission = "legacy-employee.signatures.delete";
        const string resource = "/employees/17/signature";
        var live = outcome.StartsWith("live-", StringComparison.Ordinal);
        using var transport = new ObservedTransport(evidence, async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/iam/v1/auth/check-permission", request.RequestUri!.AbsolutePath);
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var wire = document.RootElement;
            Assert.Equal(new[] { "bypassCache", "permissionId", "principalId", "resourcePath" },
                wire.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
            Assert.Equal(principal, wire.GetProperty("principalId").GetString());
            Assert.Equal(permission, wire.GetProperty("permissionId").GetString());
            Assert.Equal(resource, wire.GetProperty("resourcePath").GetString());
            Assert.Equal(live, wire.GetProperty("bypassCache").GetBoolean());
            if (live)
                Assert.Equal("evidence-test-only", Assert.Single(request.Headers.GetValues("X-Maliev-IAM-Live-Check-Key")));
            else
                Assert.False(request.Headers.Contains("X-Maliev-IAM-Live-Check-Key"));
            if (outcome == "live-unavailable") return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = outcome == "live-malformed"
                    ? new StringContent("not-json", Encoding.UTF8, "application/json")
                    : JsonContent.Create(new { principalId = resolvedPrincipal, permissionId = permission,
                        resourcePath = resource, allowed = expected, fromCache = false, latencyMs = 0 })
            };
        });
        using var http = new HttpClient(transport) { BaseAddress = new Uri("https://controlled-iam.example.invalid") };
        var client = CreateClient(http);

        var allowed = live
            ? await client.CheckPermissionLiveAsync(principal, permission, resource)
            : await client.CheckPermissionAsync(principal, permission, resource);

        evidence.AssertHealthy();
        Assert.Equal(expected, allowed);
    }

    private static IamServiceClient CreateClient(HttpClient http) => new(new NamedClientFactory(http),
        NullLogger<IamServiceClient>.Instance, new ProductionEnvironment(),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["IAM:LivePermissionChecks:Credential"] = "evidence-test-only"
        }).Build());

    private sealed class NamedClientFactory(HttpClient http) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal("IAMService", name);
            return http;
        }
    }

    private sealed class ObservedTransport(IamTransportContractEvidence evidence,
        Func<HttpRequestMessage, Task<HttpResponseMessage>> operation) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => evidence.ObserveAsync(() => operation(request), cancellationToken);
    }

    private sealed class ProductionEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "EmployeeIamTransportEvidence";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

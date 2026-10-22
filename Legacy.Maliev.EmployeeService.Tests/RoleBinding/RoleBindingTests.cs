using System.Net;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.EmployeeService.Application.Models;
using Legacy.Maliev.EmployeeService.Domain;
using Legacy.Maliev.EmployeeService.Tests.OriginalInput;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.EmployeeService.Tests.RoleBinding;

// Reuses the incumbent serialized real PG18/Redis + Production JWT/IAM transport fixture.
// No production registration, source model, global reader or historical case is changed.
/// <summary>Checks literal role-body projection against a public source-shaped reader and through candidate MVC and HTTP persistence boundaries.</summary>
/// <param name="fixture">The shared route fixture providing candidate MVC services, PostgreSQL, Redis, signed clients, and controlled IAM transport.</param>
[Collection("Employee shared role")]
public sealed class RoleBindingTests(EmployeeRouteAcceptanceFixture fixture)
    : IClassFixture<EmployeeRouteAcceptanceFixture>
{
    private const int SelectedRole = 17;
    private static readonly int[] EmployeeIds = [1001, 1002, 1003];
    private static readonly byte[] CacheSentinel = Encoding.UTF8.GetBytes("role-binding-synthetic-cache");
    private IServiceProvider Services => fixture.Factory.Services;

    /// <summary>Checks that the public source-shaped formatter retains the expected literal role text and ignores later null assignments.</summary>
    /// <param name="field">The consumed role text property, Name or Description, selected for the input case.</param>
    /// <param name="caseId">The valid scalar or ordered duplicate case selecting input JSON and expected literal role text.</param>
    /// <returns>A task that completes after asserting successful public-reader projection and zero model-state errors.</returns>
    [Theory]
    [MemberData(nameof(RoleBindingCases.Valid), MemberType = typeof(RoleBindingCases))]
    public async Task PublicMvcBaseline_UsesLiteralNullIgnoringStringProjection(string field, string caseId)
    {
        var result = await ReadReference(RoleBindingCases.ValidBody(field, caseId));
        Assert.Equal("success", result.Kind);
        Assert.Equal(0, result.ErrorCount);
        var role = Assert.IsType<SourceRole>(result.Model);
        AssertFields(field, RoleBindingCases.Expected(caseId), role.Name, role.Description);
    }

    /// <summary>Checks that the candidate MVC reader retains source-compatible nonnull role text across ordered occurrences.</summary>
    /// <param name="field">The consumed role text property, Name or Description, selected for the input case.</param>
    /// <param name="caseId">The valid scalar or ordered duplicate case selecting input JSON and expected literal role text.</param>
    /// <returns>A task that completes after asserting successful candidate role projection and zero model-state errors.</returns>
    [Theory]
    [MemberData(nameof(RoleBindingCases.Valid), MemberType = typeof(RoleBindingCases))]
    public async Task ActualMvcReader_RetainsOrderedNonNullConsumedRoleText(string field, string caseId)
    {
        var result = await ReadActual(RoleBindingCases.ValidBody(field, caseId));
        Assert.False(result.Failed);
        Assert.Equal(0, result.ErrorCount);
        var role = Assert.IsType<UpsertRoleRequest>(result.Model);
        AssertFields(field, RoleBindingCases.Expected(caseId), role.Name, role.Description);
    }

    /// <summary>Checks that an unsupported consumed role occurrence cannot be hidden in the public source-shaped formatter read.</summary>
    /// <param name="field">The consumed role text property, Name or Description, selected for the input case.</param>
    /// <param name="token">The unsupported JSON token inserted into the consumed text field.</param>
    /// <param name="position">The occurrence-order scenario placing the unsupported token relative to valid text.</param>
    /// <returns>A task that completes after asserting a failed public-reader result, no model, and recorded model-state errors.</returns>
    [Theory]
    [MemberData(nameof(RoleBindingCases.Malformed), MemberType = typeof(RoleBindingCases))]
    public async Task PublicMvcBaseline_CannotHideAnUnsupportedOccurrence(string field, string token, string position)
    {
        var result = await ReadReference(RoleBindingCases.MalformedBody(field, token, position));
        Assert.Equal("failure", result.Kind);
        Assert.Null(result.Model);
        Assert.True(result.ErrorCount > 0);
    }

    /// <summary>Checks that the candidate MVC reader refuses an unsupported role occurrence even beside valid text.</summary>
    /// <param name="field">The consumed role text property, Name or Description, selected for the input case.</param>
    /// <param name="token">The unsupported JSON token inserted into the consumed text field.</param>
    /// <param name="position">The occurrence-order scenario placing the unsupported token relative to valid text.</param>
    /// <returns>A task that completes after asserting a failed candidate read, no model, and recorded model-state errors.</returns>
    [Theory]
    [MemberData(nameof(RoleBindingCases.Malformed), MemberType = typeof(RoleBindingCases))]
    public async Task ActualMvcReader_RefusesUnsupportedOccurrenceEvenBesideValidText(string field, string token, string position)
    {
        var result = await ReadActual(RoleBindingCases.MalformedBody(field, token, position));
        Assert.True(result.Failed);
        Assert.Null(result.Model);
        Assert.True(result.ErrorCount > 0);
    }

    /// <summary>Checks that the role adapter preserves null-root handling, unrelated string parsing, minimal-API readers, and response serialization.</summary>
    [Fact]
    public void RoleRequestScope_PreservesNullRootGlobalStringsMinimalApiAndWriter()
    {
        var options = Services.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>().Value.JsonSerializerOptions;
        Assert.Null(JsonSerializer.Deserialize<UpsertRoleRequest>("null", options));
        var empty = Assert.IsType<UpsertRoleRequest>(JsonSerializer.Deserialize<UpsertRoleRequest>("{}", options));
        Assert.Null(empty.Name);
        Assert.Null(empty.Description);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<string>("true", options));
        var minimal = Services.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UpsertRoleRequest>("{\"Name\":true}", minimal));
        Assert.Equal("{\"Name\":\" literal \",\"Description\":\"fixed\"}",
            JsonSerializer.Serialize(new UpsertRoleRequest(" literal ", "fixed"), options));
        Assert.Equal("{\"Description\":\"fixed\"}",
            JsonSerializer.Serialize(new UpsertRoleRequest(null, "fixed"), options));
    }

    /// <summary>Checks literal role HTTP persistence and readback while preserving unrelated row versions and applying only linked cache eviction.</summary>
    /// <param name="field">The consumed role text property, Name or Description, selected for the input case.</param>
    /// <param name="caseId">The valid scalar or ordered duplicate case selecting input JSON and expected literal role text.</param>
    /// <param name="update">Whether to update the selected existing role with PUT instead of creating a role with POST.</param>
    /// <returns>A task that completes after asserting role fields, timestamps, created location or empty update response, unchanged unrelated rows, and scoped cache behavior.</returns>
    [Theory]
    [MemberData(nameof(RoleBindingCases.HttpValid), MemberType = typeof(RoleBindingCases))]
    public async Task HttpBinding_PersistsLiteralProjectionAndKeepsUnrelatedRowsAndCache(
        string field, string caseId, bool update)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var token = deadline.Token;
        await Seed(token);
        var before = await Snapshot(update ? SelectedRole : 0, token);
        await PrimeCache(token);
        var started = DateTime.UtcNow.AddSeconds(-5);
        using var writer = Writer(update);
        using var response = await Send(writer, update, RoleBindingCases.ValidBody(field, caseId), token);
        Assert.Equal(update ? HttpStatusCode.NoContent : HttpStatusCode.Created, response.StatusCode);
        var id = SelectedRole;
        if (!update)
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            id = document.RootElement.GetProperty("Id").GetInt32();
            Assert.True(id > 0);
            Assert.NotEqual(SelectedRole, id);
            Assert.EndsWith("/" + id, response.Headers.Location!.ToString(), StringComparison.Ordinal);
            AssertRoleJson(document.RootElement, field, RoleBindingCases.Expected(caseId));
        }
        else Assert.Empty(await response.Content.ReadAsStringAsync(token));

        await using (var db = fixture.CreateContext())
        {
            var role = await db.Roles.AsNoTracking().SingleAsync(row => row.Id == id, token);
            AssertFields(field, RoleBindingCases.Expected(caseId), role.Name, role.Description);
            var created = Assert.IsType<DateTime>(role.CreatedDate);
            var modified = Assert.IsType<DateTime>(role.ModifiedDate);
            Assert.InRange(modified, started, DateTime.UtcNow.AddSeconds(5));
            if (update) Assert.Equal(new DateTime(2020, 1, 1), created);
            else Assert.InRange(created, started, DateTime.UtcNow.AddSeconds(5));
        }

        Assert.Equal(before, await Snapshot(id, token));
        using var reader = fixture.Client("legacy-employee.roles.read");
        using var read = await reader.GetAsync("/employees/roles/" + id, token);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var fresh = JsonDocument.Parse(await read.Content.ReadAsStringAsync(token));
        AssertRoleJson(fresh.RootElement, field, RoleBindingCases.Expected(caseId));
        var cache = Services.GetRequiredService<IDistributedCache>();
        foreach (var employee in EmployeeIds)
        {
            var actual = await cache.GetAsync("employee:" + employee, token);
            if (update && employee != 1003) Assert.Null(actual);
            else Assert.Equal(CacheSentinel, actual);
        }
    }

    /// <summary>Checks that rejected role bodies produce bad requests without changing rows, concurrency versions, or cache bytes.</summary>
    /// <param name="update">Whether to submit the rejected body through role update instead of creation.</param>
    /// <param name="json">The exact malformed or otherwise rejected role request body.</param>
    /// <returns>A task that completes after asserting a bad-request response and unchanged graph and caches.</returns>
    [Theory]
    [MemberData(nameof(RoleBindingCases.HttpRejected), MemberType = typeof(RoleBindingCases))]
    public async Task HttpRejectedBody_Is400WithoutAnyRowVersionOrCacheMutation(bool update, string json)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var token = deadline.Token;
        await Seed(token);
        await PrimeCache(token);
        var before = await Snapshot(0, token);
        using var writer = Writer(update);
        using var response = await Send(writer, update, json, token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await Snapshot(0, token));
        await AssertCacheUntouched(token);
    }

    /// <summary>Checks that denied role writers cannot persist source-compatible text or invalidate employee caches.</summary>
    /// <param name="update">Whether to exercise role update instead of creation.</param>
    /// <param name="authority">The authentication or missing-permission scenario used to construct the denied writer.</param>
    /// <returns>A task that completes after asserting unauthorized or forbidden status and unchanged graph and caches.</returns>
    [Theory]
    [MemberData(nameof(RoleBindingCases.Denied), MemberType = typeof(RoleBindingCases))]
    public async Task HttpDeniedActor_CannotPersistRoleBindingOrInvalidateCache(bool update, string authority)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var token = deadline.Token;
        await Seed(token);
        await PrimeCache(token);
        var before = await Snapshot(0, token);
        using var writer = fixture.Client(
            update ? "legacy-employee.roles.update" : "legacy-employee.roles.create",
            update ? "/employees/roles/" + SelectedRole : "global", authority);
        using var response = await Send(writer, update, "{\"Name\":\"first\",\"Name\":null,\"Description\":true}", token);
        Assert.Equal(authority == "missing-permission" ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(before, await Snapshot(0, token));
        await AssertCacheUntouched(token);
    }

    /// <summary>Checks that a valid role body targeting a missing role retains not found without row or cache changes.</summary>
    /// <returns>A task that completes after asserting not found and unchanged graph and cache bytes.</returns>
    [Fact]
    public async Task MissingRole_ValidBodyKeeps404WithoutMutation()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var token = deadline.Token;
        await Seed(token);
        await PrimeCache(token);
        var before = await Snapshot(0, token);
        using var writer = fixture.Client("legacy-employee.roles.update", "/employees/roles/999999");
        using var body = new StringContent("{\"Name\":\"literal\",\"Description\":\"fixed\"}", Encoding.UTF8, "application/json");
        using var response = await writer.PutAsync("/employees/roles/999999", body, token);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, await Snapshot(0, token));
        await AssertCacheUntouched(token);
    }

    private static async Task<EmployeeBodyRead> ReadReference(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        Assert.InRange(bytes.Length, 1, 512);
        return await EmployeeOriginalInputOracle.ReadBytesAsync(EmployeeOriginalInputOracle.Formatter(),
            typeof(SourceRole), new EmptyModelMetadataProvider(), bytes, "application/json")
            .WaitAsync(TimeSpan.FromSeconds(10));
    }

    private async Task<RoleRead> ReadActual(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        Assert.InRange(bytes.Length, 1, 512);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var body = new MemoryStream(bytes, writable: false);
        var http = new DefaultHttpContext { RequestServices = Services, RequestAborted = deadline.Token };
        http.Request.Body = body;
        http.Request.ContentLength = bytes.Length;
        http.Request.ContentType = "application/json";
        var state = new ModelStateDictionary();
        var metadata = Services.GetRequiredService<IModelMetadataProvider>().GetMetadataForType(typeof(UpsertRoleRequest));
        var context = new InputFormatterContext(http, "", state, metadata,
            static (stream, encoding) => new HttpRequestStreamReader(stream, encoding), false);
        var options = Services.GetRequiredService<IOptions<MvcOptions>>().Value;
        var formatter = options.InputFormatters.FirstOrDefault(candidate => candidate.CanRead(context));
        Assert.NotNull(formatter);
        var result = await formatter.ReadAsync(context).WaitAsync(deadline.Token);
        return new RoleRead(result.HasError || !result.IsModelSet, result.Model, state.ErrorCount);
    }

    private HttpClient Writer(bool update) => fixture.Client(
        update ? "legacy-employee.roles.update" : "legacy-employee.roles.create",
        update ? "/employees/roles/" + SelectedRole : "global");

    private static async Task<HttpResponseMessage> Send(HttpClient client, bool update, string json, CancellationToken token)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return update
            ? await client.PutAsync("/employees/roles/" + SelectedRole, content, token)
            : await client.PostAsync("/employees/roles", content, token);
    }

    private async Task Seed(CancellationToken token)
    {
        fixture.Authorities.Clear();
        await using var db = fixture.CreateContext();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"Employee\", \"Address\", \"Role\", \"SignatureImageFile\" RESTART IDENTITY CASCADE", token);
        db.Roles.AddRange(
            new Role { Id = SelectedRole, Name = "Original", Description = "Original description", CreatedDate = new DateTime(2020, 1, 1), ModifiedDate = new DateTime(2020, 1, 2) },
            new Role { Id = 7, Name = "Other", Description = "Other description" });
        await db.SaveChangesAsync(token);
        db.Employees.AddRange(
            new Employee { Id = 1001, FirstName = "First", LastName = "Synthetic", Email = "first@example.test", RoleId = SelectedRole },
            new Employee { Id = 1002, FirstName = "Second", LastName = "Synthetic", Email = "second@example.test", RoleId = SelectedRole },
            new Employee { Id = 1003, FirstName = "Other", LastName = "Synthetic", Email = "other@example.test", RoleId = 7 });
        await db.SaveChangesAsync(token);
    }

    private async Task PrimeCache(CancellationToken token)
    {
        var cache = Services.GetRequiredService<IDistributedCache>();
        foreach (var employee in EmployeeIds)
        {
            await cache.SetAsync("employee:" + employee, CacheSentinel,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) }, token);
            Assert.Equal(CacheSentinel, await cache.GetAsync("employee:" + employee, token));
        }
    }

    private async Task AssertCacheUntouched(CancellationToken token)
    {
        var cache = Services.GetRequiredService<IDistributedCache>();
        foreach (var employee in EmployeeIds)
            Assert.Equal(CacheSentinel, await cache.GetAsync("employee:" + employee, token));
    }

    private async Task<string> Snapshot(int excludedRole, CancellationToken token)
    {
        await using var db = fixture.CreateContext();
        var rows = new Dictionary<string, string[]>();
        foreach (var table in new[] { "Employee", "Address", "Role", "SignatureImageFile" })
        {
            // Table names are the fixed synthetic fixture roster, never request input.
            var sql = "SELECT jsonb_build_object('row', to_jsonb(t), 'xmin', t.xmin::text)::text AS \"Value\" FROM \"" + table + "\" t";
            if (table == "Role") sql += " WHERE t.\"ID\" <> " + excludedRole.ToString(System.Globalization.CultureInfo.InvariantCulture);
            sql += " ORDER BY t.\"ID\"";
            rows[table] = await db.Database.SqlQueryRaw<string>(sql).ToArrayAsync(token);
        }
        return JsonSerializer.Serialize(rows);
    }

    private static void AssertFields(string field, string? expected, string? name, string? description)
    {
        Assert.Equal(field == "Name" ? expected : "fixed", name);
        Assert.Equal(field == "Description" ? expected : "fixed", description);
    }

    private static void AssertRoleJson(JsonElement json, string field, string? expected)
    {
        foreach (var name in new[] { "Name", "Description" })
        {
            var value = name == field ? expected : "fixed";
            if (value is null) Assert.False(json.TryGetProperty(name, out _));
            else Assert.Equal(value, json.GetProperty(name).GetString());
        }
        Assert.False(json.TryGetProperty("name", out _));
    }

    private sealed record RoleRead(bool Failed, object? Model, int ErrorCount);
}

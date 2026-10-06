using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.EmployeeService.Data;
using Legacy.Maliev.EmployeeService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.EmployeeService.Tests;

[Collection("Employee scaffold runtime")]
public sealed class EmployeeRouteAcceptanceHttpTests(EmployeeRouteAcceptanceFixture fixture)
    : IClassFixture<EmployeeRouteAcceptanceFixture>
{
    [Fact]
    public async Task Scaffold_ActualEfPreviewBuildAndGeneratedQueriesPreserveMigratedOwnedGraph()
    {
        await ResetAsync();
        var employee = await SeedGraphAsync();
        await EmployeeScaffoldRuntimeProof.RunAsync(fixture, employee.Id);
    }

    [Theory]
    [InlineData("employees", "legacy-employee.employees.create")]
    [InlineData("employees/addresses", "legacy-employee.addresses.create")]
    [InlineData("employees/roles", "legacy-employee.roles.create")]
    public async Task Create_NormalRs256AndRealPostgres_PreservesNamedLocationAndLiteralWire(string route, string permission)
    {
        await ResetAsync();
        using var client = fixture.Client(permission);
        using var response = await client.PostAsJsonAsync("/" + route, Payload(route));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        var id = root.GetProperty("Id").GetInt32();
        Assert.True(id > 0);
        Assert.EndsWith($"/{route}/{id}", response.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(root.TryGetProperty("id", out _));
        Assert.False(root.TryGetProperty("ID", out _));
        Assert.True(root.GetProperty("CreatedDate").GetDateTime() > new DateTime(2026, 1, 1));
        Assert.True(root.GetProperty("ModifiedDate").GetDateTime() > new DateTime(2026, 1, 1));
        await using var db = fixture.CreateContext();
        switch (route)
        {
            case "employees":
                Assert.Equal("วิศวกร Fixture", root.GetProperty("FullName").GetString());
                Assert.Equal("fixture@example.invalid", root.GetProperty("Email").GetString());
                Assert.False(root.TryGetProperty("PhoneNumber", out _));
                Assert.False(root.TryGetProperty("HomeAddress", out _));
                Assert.False(root.TryGetProperty("Role", out _));
                Assert.False(root.TryGetProperty("Password", out _));
                var employee = await db.Employees.SingleAsync();
                Assert.Equal(id, employee.Id); Assert.Equal("วิศวกร", employee.FirstName); break;
            case "employees/addresses":
                Assert.Equal("ถนน Fixture", root.GetProperty("AddressLine1").GetString());
                Assert.Equal(764, root.GetProperty("CountryId").GetInt32());
                Assert.False(root.TryGetProperty("Building", out _));
                Assert.False(root.TryGetProperty("Employee", out _));
                Assert.Equal(id, (await db.Addresses.SingleAsync()).Id); break;
            default:
                Assert.Equal("Engineer", root.GetProperty("Name").GetString());
                Assert.False(root.TryGetProperty("Description", out _));
                Assert.Equal(id, (await db.Roles.SingleAsync()).Id); break;
        }
    }

    [Theory]
    [InlineData("employees/addresses", "legacy-employee.addresses.create")]
    [InlineData("employees/roles", "legacy-employee.roles.create")]
    public async Task Create_AllSourceOptionalFieldsAbsent_PersistsWithoutInventedRequiredFields(string route, string permission)
    {
        await ResetAsync();
        using var client = fixture.Client(permission);
        using var response = await client.PostAsJsonAsync("/" + route, new { });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(document.RootElement.TryGetProperty(route.EndsWith("roles", StringComparison.Ordinal) ? "Name" : "AddressLine1", out _));
    }

    [Theory]
    [InlineData("employees", "legacy-employee.employees.create")]
    [InlineData("employees/addresses", "legacy-employee.addresses.create")]
    [InlineData("employees/roles", "legacy-employee.roles.create")]
    public async Task Create_NullOrMalformedBody_IsBadRequestWithoutPersistence(string route, string permission)
    {
        await ResetAsync(); using var client = fixture.Client(permission);
        foreach (var body in new[] { "null", "{broken-json" })
        {
            using var response = await client.PostAsync("/" + route, Json(body));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        await using var db = fixture.CreateContext();
        Assert.False(await db.Employees.AnyAsync()); Assert.False(await db.Addresses.AnyAsync()); Assert.False(await db.Roles.AnyAsync());
    }

    [Theory]
    [InlineData("{\"FirstName\":\"\",\"LastName\":\"Fixture\",\"Email\":\"x@example.invalid\"}")]
    [InlineData("{\"FirstName\":\"Fixture\",\"LastName\":\" \",\"Email\":\"x@example.invalid\"}")]
    [InlineData("{\"FirstName\":\"Fixture\",\"LastName\":\"Fixture\",\"Email\":\"not-email\"}")]
    public async Task EmployeeCreate_CurrentApprovedValidation_RefusesBeforeWrite(string body)
    {
        await ResetAsync(); using var client = fixture.Client("legacy-employee.employees.create");
        using var response = await client.PostAsync("/employees", Json(body));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var db = fixture.CreateContext(); Assert.False(await db.Employees.AnyAsync());
    }

    [Theory]
    [InlineData("employees", "legacy-employee.employees.read")]
    [InlineData("employees/addresses", "legacy-employee.addresses.read")]
    [InlineData("employees/roles", "legacy-employee.roles.read")]
    [InlineData("employees/signatures", "legacy-employee.signatures.read")]
    public async Task Get_MissingLiteralId_IsNotFoundWithoutMutation(string route, string permission)
    {
        await ResetAsync();
        var resource = route == "employees/roles" ? "global" : route == "employees/signatures"
            ? "/employees/2147483647/signature" : $"/{route}/2147483647";
        using var client = fixture.Client(permission, resource);
        using var response = await client.GetAsync($"/{route}/2147483647");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var db = fixture.CreateContext(); Assert.False(await db.Employees.AnyAsync());
    }

    [Theory]
    [InlineData("employees/addresses", "legacy-employee.addresses.list")]
    [InlineData("employees/roles", "legacy-employee.roles.read")]
    public async Task List_EmptySourceCollection_IsNotFound(string route, string permission)
    {
        await ResetAsync(); using var client = fixture.Client(permission);
        using var response = await client.GetAsync("/" + route);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("employees/addresses", "legacy-employee.addresses.list")]
    [InlineData("employees/roles", "legacy-employee.roles.read")]
    public async Task List_NonemptySourceCollection_ReturnsLiteralPascalCaseAndNullOmission(string route, string permission)
    {
        await ResetAsync(); await SeedGraphAsync(); using var client = fixture.Client(permission);
        using var response = await client.GetAsync("/" + route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var row = Assert.Single(document.RootElement.EnumerateArray());
        Assert.True(row.GetProperty("Id").GetInt32() > 0);
        Assert.False(row.TryGetProperty(route.EndsWith("roles", StringComparison.Ordinal) ? "Description" : "Building", out _));
    }

    [Fact]
    public async Task Get_EmployeeWithAddress_EmbedsExactHomeAddressWithoutCyclesOrInventedRole()
    {
        await ResetAsync(); var employee = await SeedGraphAsync();
        using var client = fixture.Client("legacy-employee.employees.read", $"/employees/{employee.Id}");
        using var response = await client.GetAsync($"/employees/{employee.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var json = document.RootElement;
        Assert.Equal(employee.Id, json.GetProperty("Id").GetInt32());
        Assert.Equal(employee.HomeAddressId, json.GetProperty("HomeAddressId").GetInt32());
        var address = json.GetProperty("HomeAddress");
        Assert.Equal("Original road", address.GetProperty("AddressLine1").GetString());
        Assert.False(address.TryGetProperty("Employee", out _));
        Assert.False(address.TryGetProperty("Employees", out _));
        Assert.False(json.TryGetProperty("Role", out _)); // Source GET includes HomeAddress only.
    }

    [Theory]
    [InlineData("employee")]
    [InlineData("address")]
    [InlineData("role")]
    [InlineData("profile")]
    public async Task Update_RealRepositoryAndRedis_InvalidatesAffectedEmployeeProjectionAndPreservesCreatedDate(string kind)
    {
        await ResetAsync(); var original = await SeedGraphAsync();
        using var read = fixture.Client("legacy-employee.employees.read", $"/employees/{original.Id}");
        using var before = await read.GetAsync($"/employees/{original.Id}"); Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        var (route, permission, resource, body) = Update(kind, original);
        using var writer = fixture.Client(permission, resource);
        using var response = await writer.PutAsJsonAsync(route, body);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var db = fixture.CreateContext();
        var persisted = await db.Employees.AsNoTracking().SingleAsync();
        Assert.Equal(original.CreatedDate, persisted.CreatedDate);
        Assert.Equal("fixture@example.invalid", persisted.Email);
        using var after = await read.GetAsync($"/employees/{original.Id}"); Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        using var document = JsonDocument.Parse(await after.Content.ReadAsStringAsync());
        if (kind is "employee" or "profile")
        {
            Assert.Equal("Updated", document.RootElement.GetProperty("FirstName").GetString());
            Assert.Equal("Updated", persisted.FirstName);
            Assert.Equal(original.RoleId, persisted.RoleId); Assert.Equal(original.HomeAddressId, persisted.HomeAddressId);
        }
        if (kind == "address") Assert.Equal("Updated road", document.RootElement.GetProperty("HomeAddress").GetProperty("AddressLine1").GetString());
        if (kind == "role") Assert.Equal("Updated role", (await db.Roles.SingleAsync()).Name);
    }

    [Theory]
    [InlineData("employee")]
    [InlineData("address")]
    [InlineData("role")]
    [InlineData("profile")]
    public async Task Update_MissingRecord_IsNotFoundWithoutInventedRows(string kind)
    {
        await ResetAsync(); var missing = new Employee { Id = int.MaxValue, HomeAddressId = int.MaxValue, RoleId = int.MaxValue };
        var (route, permission, resource, body) = Update(kind, missing);
        using var client = fixture.Client(permission, resource); using var response = await client.PutAsJsonAsync(route, body);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var db = fixture.CreateContext(); Assert.False(await db.Employees.AnyAsync());
        Assert.False(await db.Addresses.AnyAsync()); Assert.False(await db.Roles.AnyAsync());
    }

    [Theory]
    [InlineData("employees", "legacy-employee.employees.delete")]
    [InlineData("employees/addresses", "legacy-employee.addresses.delete")]
    [InlineData("employees/roles", "legacy-employee.roles.delete")]
    [InlineData("employees/signatures", "legacy-employee.signatures.delete")]
    public async Task Delete_NormalRs256ControlledRemoteIamAndRealPostgres_DeletesOnlySelectedMetadataThenNotFound(string route, string permission)
    {
        await ResetAsync(); var graph = await SeedGraphAsync(linked: false);
        var id = route.EndsWith("addresses", StringComparison.Ordinal) ? graph.HomeAddress!.Id
            : route.EndsWith("roles", StringComparison.Ordinal) ? graph.Role!.Id : graph.Id;
        var resource = route.EndsWith("signatures", StringComparison.Ordinal) ? $"/employees/{id}/signature" : $"/{route}/{id}";
        using var client = fixture.Client(permission, resource);
        using var first = await client.DeleteAsync($"/{route}/{id}"); Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        using var second = await client.DeleteAsync($"/{route}/{id}"); Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        await using var db = fixture.CreateContext();
        Assert.Equal(route == "employees" ? 0 : 1, await db.Employees.CountAsync());
        Assert.Equal(route.EndsWith("addresses", StringComparison.Ordinal) ? 0 : 1, await db.Addresses.CountAsync());
        Assert.Equal(route.EndsWith("roles", StringComparison.Ordinal) ? 0 : 1, await db.Roles.CountAsync());
        Assert.Equal(route.EndsWith("signatures", StringComparison.Ordinal) ? 0 : 1, await db.SignatureImageFiles.CountAsync());
        var authority = fixture.Authorities.Values.Single(item => item.Permission == permission && item.LiveCalls == 2);
        Assert.Equal(2, authority.LiveCalls); // Forced live bypasses actual client's previous positive cache.
    }

    [Theory]
    [InlineData("deny")]
    [InlineData("unavailable")]
    [InlineData("malformed")]
    [InlineData("no-client")]
    public async Task Delete_ForcedLiveDenialFaultOrMissingClient_RefusesDespiteSignedPermissionWithoutEffects(string decision)
    {
        await ResetAsync(); var employee = await SeedGraphAsync();
        using var factory = decision == "no-client" ? fixture.NewFactory(false) : null;
        using var client = fixture.Client("legacy-employee.employees.delete", $"/employees/{employee.Id}", decision: decision, factory: factory);
        using var response = await client.DeleteAsync($"/employees/{employee.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var db = fixture.CreateContext(); Assert.Equal(employee.FirstName, (await db.Employees.SingleAsync()).FirstName);
        Assert.Equal(1, await db.Addresses.CountAsync()); Assert.Equal(1, await db.Roles.CountAsync()); Assert.Equal(1, await db.SignatureImageFiles.CountAsync());
    }

    [Theory]
    [InlineData("employees", "legacy-employee.employees.create", "anonymous", 401)]
    [InlineData("employees", "legacy-employee.employees.create", "expired", 401)]
    [InlineData("employees", "legacy-employee.employees.create", "wrong-signature", 401)]
    [InlineData("employees", "legacy-employee.employees.create", "wrong-issuer", 401)]
    [InlineData("employees", "legacy-employee.employees.create", "wrong-audience", 401)]
    [InlineData("employees", "legacy-employee.employees.create", "missing-permission", 403)]
    [InlineData("employees/addresses", "legacy-employee.addresses.create", "missing-permission", 403)]
    [InlineData("employees/roles", "legacy-employee.roles.create", "missing-permission", 403)]
    public async Task Create_NormalAuthFailures_HaveNoPersistence(string route, string permission, string authority, int status)
    {
        await ResetAsync(); using var client = fixture.Client(permission, authority: authority);
        using var response = await client.PostAsJsonAsync("/" + route, Payload(route));
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        await using var db = fixture.CreateContext(); Assert.False(await db.Employees.AnyAsync()); Assert.False(await db.Addresses.AnyAsync()); Assert.False(await db.Roles.AnyAsync());
    }

    [Fact]
    public async Task Signature_CreateQueryNamedLocationAndBodyUpdate_PreserveSourceEmployeeIdReassignmentWithoutCloudIo()
    {
        await ResetAsync(); var employee = await SeedGraphAsync(withSignature: false);
        await using var db = fixture.CreateContext();
        var other = new Employee { FirstName = "Other", LastName = "Fixture", Email = "other@example.invalid" };
        db.Employees.Add(other); await db.SaveChangesAsync();
        using var writer = fixture.Client("legacy-employee.signatures.write", $"/employees/{employee.Id}");
        using var created = await writer.PostAsync($"/employees/{employee.Id}/signatures?bucket=fixture-private&objectName=signatures%2Fpart.png", null);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.EndsWith($"/employees/Signatures/{employee.Id}", created.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
        using var initial = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var signatureId = initial.RootElement.GetProperty("Id").GetInt32();
        Assert.Equal(employee.Id, initial.RootElement.GetProperty("EmployeeId").GetInt32());
        Assert.Equal("signatures/part.png", initial.RootElement.GetProperty("ObjectName").GetString());
        var date = (await db.SignatureImageFiles.AsNoTracking().SingleAsync()).CreatedDate;
        using var updater = fixture.Client("legacy-employee.signatures.write", $"/employees/{employee.Id}/signature");
        using var updated = await updater.PutAsJsonAsync($"/employees/signatures/{employee.Id}", new { Bucket = "fixture-private", ObjectName = "signatures/updated.png", EmployeeId = other.Id });
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
        var persisted = await db.SignatureImageFiles.AsNoTracking().SingleAsync();
        Assert.Equal(signatureId, persisted.Id); Assert.Equal(date, persisted.CreatedDate); Assert.Equal(other.Id, persisted.EmployeeId);
        using var readOld = fixture.Client("legacy-employee.signatures.read", $"/employees/{employee.Id}/signature");
        using var absent = await readOld.GetAsync($"/employees/signatures/{employee.Id}"); Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        using var readNew = fixture.Client("legacy-employee.signatures.read", $"/employees/{other.Id}/signature");
        using var found = await readNew.GetAsync($"/employees/signatures/{other.Id}"); Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        using var document = JsonDocument.Parse(await found.Content.ReadAsStringAsync());
        Assert.Equal(signatureId, document.RootElement.GetProperty("Id").GetInt32());
        Assert.Equal("signatures/updated.png", document.RootElement.GetProperty("ObjectName").GetString());
    }

    [Theory]
    [InlineData("", "object")]
    [InlineData("bucket", "")]
    public async Task Signature_CreateMissingQueryValue_BadRequestWithoutMetadata(string bucket, string objectName)
    {
        await ResetAsync(); var employee = await SeedGraphAsync(withSignature: false);
        using var client = fixture.Client("legacy-employee.signatures.write", $"/employees/{employee.Id}");
        using var response = await client.PostAsync($"/employees/{employee.Id}/signatures?bucket={bucket}&objectName={objectName}", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var db = fixture.CreateContext(); Assert.False(await db.SignatureImageFiles.AnyAsync());
    }

    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    public async Task Signature_MissingEmployeeOrSignature_IsNotFoundWithoutInventedRecord(string action)
    {
        await ResetAsync(); using var client = fixture.Client("legacy-employee.signatures.write", action == "create"
            ? "/employees/2147483647" : "/employees/2147483647/signature");
        using var response = action == "create" ? await client.PostAsync("/employees/2147483647/signatures?bucket=fixture&objectName=part.png", null)
            : await client.PutAsJsonAsync("/employees/signatures/2147483647", new { Bucket = "fixture", ObjectName = "part.png" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var db = fixture.CreateContext(); Assert.False(await db.SignatureImageFiles.AnyAsync());
    }

    [Theory]
    [InlineData("{\"Bucket\":\"\",\"ObjectName\":\"part.png\"}")]
    [InlineData("{\"Bucket\":\"fixture\",\"ObjectName\":\"\"}")]
    [InlineData("null")]
    public async Task Signature_UpdateInvalidBody_BadRequestPreservesOriginalMetadata(string body)
    {
        await ResetAsync(); var employee = await SeedGraphAsync(); using var client = fixture.Client("legacy-employee.signatures.write", $"/employees/{employee.Id}/signature");
        using var response = await client.PutAsync($"/employees/signatures/{employee.Id}", Json(body));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var db = fixture.CreateContext(); Assert.Equal("signatures/original.png", (await db.SignatureImageFiles.SingleAsync()).ObjectName);
    }

    [Theory]
    [InlineData("{\"FirstName\":\"\",\"LastName\":\"Fixture\"}")]
    [InlineData("{\"FirstName\":\"Fixture\",\"LastName\":\"\"}")]
    [InlineData("{\"FirstName\":\"Fixture\",\"LastName\":\"Fixture\",\"RoleId\":9}")]
    [InlineData("{\"FirstName\":\"Fixture\",\"LastName\":\"Fixture\",\"Email\":\"other@example.invalid\"}")]
    public async Task SelfProfile_InvalidOrExpandedBody_IsRejectedWithoutChangingAdministrativeFields(string body)
    {
        await ResetAsync(); var employee = await SeedGraphAsync(); using var client = fixture.Client("legacy-employee.employees.self-update", $"/employees/{employee.Id}/profile");
        using var response = await client.PutAsync($"/employees/{employee.Id}/profile", Json(body));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var db = fixture.CreateContext(); var after = await db.Employees.AsNoTracking().SingleAsync();
        Assert.Equal(employee.FirstName, after.FirstName); Assert.Equal(employee.Email, after.Email); Assert.Equal(employee.RoleId, after.RoleId); Assert.Equal(employee.ModifiedDate, after.ModifiedDate);
    }

    [Theory]
    [InlineData("employee")]
    [InlineData("profile")]
    [InlineData("address")]
    [InlineData("delete")]
    public async Task Cache_LatePreMutationReadCannotResurrectStaleProjectionInRealRedis(string kind)
    {
        await ResetAsync();
        var employee = await SeedGraphAsync();
        var schedule = new DatabaseReadSchedule();
        using var factory = fixture.DatabaseRaceFactory(schedule);
        using var reader = fixture.Client("legacy-employee.employees.read", $"/employees/{employee.Id}", factory: factory);
        var oldRead = reader.GetAsync($"/employees/{employee.Id}");
        await schedule.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        try
        {
            if (kind == "delete")
            {
                using var writer = fixture.Client("legacy-employee.employees.delete", $"/employees/{employee.Id}", factory: factory);
                using var written = await writer.DeleteAsync($"/employees/{employee.Id}");
                Assert.Equal(HttpStatusCode.NoContent, written.StatusCode);
            }
            else
            {
                var (route, permission, resource, body) = Update(kind, employee);
                using var writer = fixture.Client(permission, resource, factory: factory);
                using var written = await writer.PutAsJsonAsync(route, body);
                Assert.Equal(HttpStatusCode.NoContent, written.StatusCode);
            }
            await using var db = fixture.CreateContext();
            if (kind == "delete") Assert.False(await db.Employees.AnyAsync());
            else if (kind == "address") Assert.Equal("Updated road", (await db.Addresses.SingleAsync()).AddressLine1);
            else Assert.Equal("Updated", (await db.Employees.SingleAsync()).FirstName);
        }
        finally { schedule.Release.TrySetResult(); }
        // The already-started read may return its historical snapshot. A subsequent
        // completed request must not revive it after the acknowledged mutation.
        using var historical = await oldRead.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(HttpStatusCode.OK, historical.StatusCode);
        // Adversarial late fill from an old writer remains possible during rollout.
        // Ordinary detail reads must ignore those real Redis bytes, including deletion.
        using (var scope = factory.Services.CreateScope())
        {
            var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
            using var oldJson = JsonDocument.Parse(await historical.Content.ReadAsStringAsync());
            var stale = JsonSerializer.SerializeToUtf8Bytes(new
            {
                employee.Id,
                FirstName = "Original",
                LastName = "Fixture",
                FullName = "Original Fixture",
                Email = "fixture@example.invalid",
                HomeAddress = new { AddressLine1 = "Original road" },
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            await cache.SetAsync($"employee:{employee.Id}", stale, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) });
            Assert.Equal(stale, await cache.GetAsync($"employee:{employee.Id}"));
        }
        using var current = await reader.GetAsync($"/employees/{employee.Id}");
        if (kind == "delete") Assert.Equal(HttpStatusCode.NotFound, current.StatusCode);
        else
        {
            Assert.Equal(HttpStatusCode.OK, current.StatusCode);
            using var document = JsonDocument.Parse(await current.Content.ReadAsStringAsync());
            if (kind == "address") Assert.Equal("Updated road", document.RootElement.GetProperty("HomeAddress").GetProperty("AddressLine1").GetString());
            else Assert.Equal("Updated", document.RootElement.GetProperty("FirstName").GetString());
        }
    }

    [Fact]
    public async Task Documentation_ExistingNonProductionHttpContractContainsBusinessRoutesAndProductionDoesNotExposeIt()
    {
        using var production = fixture.Factory.CreateClient();
        using var hidden = await production.GetAsync("/employee/openapi/v1.json");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using var docsFactory = fixture.DocumentationFactory();
        using var docs = docsFactory.CreateClient();
        using var response = await docs.GetAsync("/employee/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths").EnumerateObject().ToDictionary(item => item.Name, item => item.Value, StringComparer.OrdinalIgnoreCase);
        Assert.True(paths["/Employees"].TryGetProperty("get", out _));
        Assert.True(paths["/Employees"].TryGetProperty("post", out _));
        Assert.True(paths["/Employees/{employeeId}/profile"].TryGetProperty("put", out _));
        Assert.True(paths["/employees/Addresses/{addressId}"].TryGetProperty("delete", out _));
        Assert.True(paths["/employees/Roles/{roleId}"].TryGetProperty("put", out _));
        Assert.True(paths["/employees/Signatures/{employeeId}"].TryGetProperty("get", out _));
        Assert.True(paths["/employees/{employeeId}/Signatures"].TryGetProperty("post", out var signature));
        var query = signature.GetProperty("parameters").EnumerateArray().Where(item => item.GetProperty("in").GetString() == "query").Select(item => item.GetProperty("name").GetString()).ToArray();
        Assert.Contains("bucket", query); Assert.Contains("objectName", query);
        Assert.DoesNotContain(paths.Keys, path => path.Contains("identities", StringComparison.OrdinalIgnoreCase));
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var profile = schemas.GetProperty("UpdateEmployeeSelfProfileRequest").GetProperty("properties");
        Assert.True(profile.TryGetProperty("FirstName", out _), "Actual documented property names: " + string.Join(", ", profile.EnumerateObject().Select(item => item.Name)));
        Assert.True(profile.TryGetProperty("LastName", out _));
        Assert.True(profile.TryGetProperty("PhoneNumber", out _));
        Assert.False(profile.TryGetProperty("RoleId", out _));
        Assert.False(profile.TryGetProperty("Email", out _));
        var expected = new Dictionary<string, string[]>
        {
            ["UpsertEmployeeRequest"] = ["RoleId", "FirstName", "LastName", "PhoneNumber", "Email", "DateOfBirth", "HomeAddressId"],
            ["UpdateEmployeeSelfProfileRequest"] = ["FirstName", "LastName", "PhoneNumber", "DateOfBirth"],
            ["UpsertAddressRequest"] = ["Building", "AddressLine1", "AddressLine2", "City", "State", "PostalCode", "CountryId"],
            ["UpsertRoleRequest"] = ["Name", "Description"],
            ["UpsertSignatureImageFileRequest"] = ["Bucket", "ObjectName", "EmployeeId"],
            ["EmployeeResponse"] = ["Id", "RoleId", "FirstName", "LastName", "FullName", "PhoneNumber", "Email", "DateOfBirth", "HomeAddressId", "CreatedDate", "ModifiedDate", "HomeAddress", "Role"],
            ["AddressResponse"] = ["Id", "Building", "AddressLine1", "AddressLine2", "City", "State", "PostalCode", "CountryId", "CreatedDate", "ModifiedDate"],
            ["RoleResponse"] = ["Id", "Name", "Description", "CreatedDate", "ModifiedDate"],
            ["SignatureImageFileResponse"] = ["Id", "EmployeeId", "Bucket", "ObjectName", "CreatedDate", "ModifiedDate"],
        };
        foreach (var (name, properties) in expected)
        {
            var actual = schemas.GetProperty(name).GetProperty("properties").EnumerateObject().Select(item => item.Name).Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(properties.Order(StringComparer.Ordinal).ToArray(), actual);
        }
        var page = Assert.Single(schemas.EnumerateObject(), item => item.Value.TryGetProperty("properties", out var properties)
            && properties.TryGetProperty("Items", out _));
        Assert.Equal(new[] { "HasNextPage", "HasPreviousPage", "Items", "PageIndex", "TotalPages", "TotalRecords" },
            page.Value.GetProperty("properties").EnumerateObject().Select(item => item.Name).Order(StringComparer.Ordinal).ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Detail_PoisonedRedisAndDeniedCacheCannotReplaceCommittedPostgres(bool denyRemoval)
    {
        await ResetAsync(); var employee = await SeedGraphAsync();
        var key = $"employee:{employee.Id}";
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var actual = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
            await actual.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(new { employee.Id, FirstName = "Poisoned" }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) });
        }
        var schedule = new CachePublishSchedule(key) { DenyReads = true, DenyRemovals = denyRemoval };
        using var factory = fixture.CacheRaceFactory(schedule);
        using var reader = fixture.Client("legacy-employee.employees.read", $"/employees/{employee.Id}", factory: factory);
        if (denyRemoval)
        {
            var (route, permission, resource, body) = Update("employee", employee);
            using var writer = fixture.Client(permission, resource, factory: factory);
            using var response = await writer.PutAsJsonAsync(route, body);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal(1, schedule.RemoveAttempts);
            await using var db = fixture.CreateContext();
            Assert.Equal("Updated", (await db.Employees.SingleAsync()).FirstName);
        }
        using var detail = await reader.GetAsync($"/employees/{employee.Id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var json = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        Assert.Equal(denyRemoval ? "Updated" : "Original", json.RootElement.GetProperty("FirstName").GetString());
        Assert.Equal(0, schedule.ReadAttempts); Assert.Equal(0, schedule.Publications);
    }

    private async Task ResetAsync()
    {
        await using var db = fixture.CreateContext();
        var ids = await db.Employees.Select(item => item.Id).ToArrayAsync();
        using var scope = fixture.Factory.Services.CreateScope(); var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        foreach (var id in ids) await cache.RemoveAsync($"employee:{id}");
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"Employee\", \"Address\", \"Role\", \"SignatureImageFile\" CASCADE");
        fixture.Authorities.Clear();
    }

    private async Task<Employee> SeedGraphAsync(bool linked = true, bool withSignature = true)
    {
        await using var db = fixture.CreateContext();
        var address = new Address { AddressLine1 = "Original road", CountryId = 764 };
        var role = new Role { Name = "Original role" };
        db.Addresses.Add(address); db.Roles.Add(role); await db.SaveChangesAsync();
        var employee = new Employee
        {
            FirstName = "Original",
            LastName = "Fixture",
            PhoneNumber = "0690",
            Email = "fixture@example.invalid",
            HomeAddressId = linked ? address.Id : null,
            RoleId = linked ? role.Id : null,
            CreatedDate = new DateTime(2020, 1, 1),
            ModifiedDate = new DateTime(2020, 1, 2)
        };
        db.Employees.Add(employee); await db.SaveChangesAsync();
        if (withSignature)
        {
            db.SignatureImageFiles.Add(new SignatureImageFile { EmployeeId = employee.Id, Bucket = "fixture-private", ObjectName = "signatures/original.png" });
            await db.SaveChangesAsync();
        }
        // The detached fixture-only references select independent delete targets;
        // they do not persist an invented relationship for the unlinked delete controls.
        employee.HomeAddress = address; employee.Role = role;
        return employee;
    }

    private static object Payload(string route) => route switch
    {
        "employees" => new { FirstName = "วิศวกร", LastName = "Fixture", Email = "fixture@example.invalid" },
        "employees/addresses" => new { AddressLine1 = "ถนน Fixture", CountryId = 764 },
        _ => new { Name = "Engineer" },
    };

    private static (string Route, string Permission, string Resource, object Body) Update(string kind, Employee employee) => kind switch
    {
        "employee" => ($"/employees/{employee.Id}", "legacy-employee.employees.update", $"/employees/{employee.Id}",
            new { FirstName = "Updated", LastName = "Fixture", Email = "fixture@example.invalid", employee.RoleId, employee.HomeAddressId }),
        "profile" => ($"/employees/{employee.Id}/profile", "legacy-employee.employees.self-update", $"/employees/{employee.Id}/profile",
            new { FirstName = "Updated", LastName = "Fixture", PhoneNumber = "0808" }),
        "address" => ($"/employees/addresses/{employee.HomeAddressId}", "legacy-employee.addresses.update", $"/employees/addresses/{employee.HomeAddressId}",
            new { AddressLine1 = "Updated road", CountryId = 764 }),
        _ => ($"/employees/roles/{employee.RoleId}", "legacy-employee.roles.update", $"/employees/roles/{employee.RoleId}", new { Name = "Updated role" }),
    };

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");
}

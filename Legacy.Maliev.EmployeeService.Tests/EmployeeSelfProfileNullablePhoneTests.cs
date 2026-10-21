using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.EmployeeService.Api.Authorization;
using Legacy.Maliev.EmployeeService.Api.Controllers;
using Legacy.Maliev.EmployeeService.Application.Interfaces;
using Legacy.Maliev.EmployeeService.Application.Models;
using Legacy.Maliev.EmployeeService.Data;
using Legacy.Maliev.EmployeeService.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;

namespace Legacy.Maliev.EmployeeService.Tests;

/// <summary>Checks optional self-profile phone updates, narrow field ownership, cache refresh, and authorization rejection.</summary>
/// <param name="fixture">The fixture owning disposable PostgreSQL, the controlled-clock host, and synthetic signing keys for profile tests.</param>
public sealed class EmployeeSelfProfileNullablePhoneTests(NullablePhoneFixture fixture)
    : IClassFixture<NullablePhoneFixture>
{
    /// <summary>Checks direct profile updates preserve optional phone values while changing only employee-owned fields.</summary>
    /// <param name="phone">The optional phone value to persist: null, empty text, or a supported phone string.</param>
    /// <returns>A task that completes after verifying direct profile updates preserve optional phone values while changing only employee-owned fields.</returns>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0690")]
    public async Task Controller_OptionalPhone_PersistsOnlyOwnedProfileFields(string? phone)
    {
        var original = await fixture.SeedAsync();
        using var scope = fixture.Factory.Services.CreateScope();
        var controller = new EmployeesController(scope.ServiceProvider.GetRequiredService<IEmployeeService>());

        var result = await controller.UpdateSelfProfileAsync(original.Id,
            new UpdateEmployeeSelfProfileRequest("  New  ", "  Name  ", phone, null), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        var persisted = await fixture.ReadAsync(original.Id);
        Assert.Equal(phone, persisted.PhoneNumber);
        Assert.Equal("New", persisted.FirstName);
        Assert.Equal("Name", persisted.LastName);
        Assert.Null(persisted.DateOfBirth);
        AssertManagedFieldsUnchanged(original, persisted);
    }

    /// <summary>Checks that both an omitted phone member and an explicit null clear an existing phone.</summary>
    /// <param name="omitPhone">Whether to omit PhoneNumber from the payload rather than send it explicitly as null.</param>
    /// <returns>A task that completes after verifying that both an omitted phone member and an explicit null clear an existing phone.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Http_NullOrOmittedPhone_ClearsExistingPhone(bool omitPhone)
    {
        var original = await fixture.SeedAsync();
        var payload = ProfilePayload();
        if (omitPhone) payload.Remove("PhoneNumber");

        using var response = await PutAsync(original.Id, payload);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var persisted = await fixture.ReadAsync(original.Id);
        Assert.Null(persisted.PhoneNumber);
        Assert.Equal("New", persisted.FirstName);
        AssertManagedFieldsUnchanged(original, persisted);
    }

    /// <summary>Checks that an already absent phone does not prevent another owned profile field from changing.</summary>
    /// <returns>A task that completes after verifying that an already absent phone does not prevent another owned profile field from changing.</returns>
    [Fact]
    public async Task Http_AlreadyAbsentPhone_AllowsAnotherProfileFieldToChange()
    {
        var original = await fixture.SeedAsync(phone: null);

        using var response = await PutAsync(original.Id, ProfilePayload());

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var persisted = await fixture.ReadAsync(original.Id);
        Assert.Null(persisted.PhoneNumber);
        Assert.Equal("New", persisted.FirstName);
        AssertManagedFieldsUnchanged(original, persisted);
    }

    /// <summary>Checks that clearing a phone invalidates a previously cached employee read.</summary>
    /// <returns>A task that completes after verifying that clearing a phone invalidates a previously cached employee read.</returns>
    [Fact]
    public async Task Http_ClearedPhone_InvalidatesPreviouslyCachedEmployeeRead()
    {
        var original = await fixture.SeedAsync();
        using var client = fixture.CreateClient();
        using var initial = await client.GetAsync($"/employees/{original.Id}");
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        using var before = JsonDocument.Parse(await initial.Content.ReadAsStringAsync());
        Assert.Equal("0404", before.RootElement.GetProperty("PhoneNumber").GetString());

        using var response = await PutAsync(original.Id, ProfilePayload());
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var final = await client.GetAsync($"/employees/{original.Id}");
        Assert.Equal(HttpStatusCode.OK, final.StatusCode);
        using var after = JsonDocument.Parse(await final.Content.ReadAsStringAsync());
        Assert.False(after.RootElement.TryGetProperty("PhoneNumber", out _));
        Assert.Equal("New", after.RootElement.GetProperty("FirstName").GetString());
    }

    /// <summary>Checks that empty and 256-character phone values are accepted without changing managed fields.</summary>
    /// <param name="length">The accepted phone-string length, either zero or 256 characters.</param>
    /// <returns>A task that completes after verifying that empty and 256-character phone values are accepted without changing managed fields.</returns>
    [Theory]
    [InlineData(0)]
    [InlineData(256)]
    public async Task Http_PhoneAtSupportedLengthBoundary_IsAccepted(int length)
    {
        var original = await fixture.SeedAsync();
        var payload = ProfilePayload();
        payload["PhoneNumber"] = new string('0', length);

        using var response = await PutAsync(original.Id, payload);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var persisted = await fixture.ReadAsync(original.Id);
        Assert.Equal(new string('0', length), persisted.PhoneNumber);
        AssertManagedFieldsUnchanged(original, persisted);
    }

    /// <summary>Checks that blank names and oversized names or phone values return bad request without persistence.</summary>
    /// <param name="field">The owned field made invalid: FirstName, LastName, or PhoneNumber.</param>
    /// <param name="length">Zero selects a whitespace-only name; 257 selects an overlong string.</param>
    /// <returns>A task that completes after verifying that blank names and oversized names or phone values return bad request without persistence.</returns>
    [Theory]
    [InlineData("FirstName", 0)]
    [InlineData("LastName", 0)]
    [InlineData("FirstName", 257)]
    [InlineData("LastName", 257)]
    [InlineData("PhoneNumber", 257)]
    public async Task Http_InvalidNameOrOversizedPhone_DoesNotPersist(string field, int length)
    {
        var original = await fixture.SeedAsync();
        var payload = ProfilePayload();
        payload["PhoneNumber"] = "0690";
        payload[field] = length == 0 ? "  " : new string('x', length);

        using var response = await PutAsync(original.Id, payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(original, await fixture.ReadAsync(original.Id));
    }

    /// <summary>Checks that administrative or owner fields outside the narrow profile payload are rejected without changing either employee.</summary>
    /// <param name="field">The forbidden payload member: RoleId, Email, HomeAddressId, or EmployeeId.</param>
    /// <returns>A task that completes after verifying that administrative or owner fields outside the narrow profile payload are rejected without changing either employee.</returns>
    [Theory]
    [InlineData("RoleId")]
    [InlineData("Email")]
    [InlineData("HomeAddressId")]
    [InlineData("EmployeeId")]
    public async Task Http_ExpandedAdministrativeOrOwnerPayload_IsRejected(string field)
    {
        var original = await fixture.SeedAsync();
        var other = await fixture.SeedAsync();
        var payload = ProfilePayload();
        payload["PhoneNumber"] = "0690";
        payload[field] = field == "Email" ? "other@example.invalid" : other.Id;

        using var response = await PutAsync(original.Id, payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(original, await fixture.ReadAsync(original.Id));
        Assert.Equal(other, await fixture.ReadAsync(other.Id));
    }

    /// <summary>Checks that untrusted, expired, or insufficiently permitted clients cannot persist profile changes.</summary>
    /// <param name="authority">The rejected caller scenario: anonymous, missing-permission, expired, or wrong-signature.</param>
    /// <param name="expected">The expected unauthorized or forbidden response for the selected authority.</param>
    /// <returns>A task that completes after verifying that untrusted, expired, or insufficiently permitted clients cannot persist profile changes.</returns>
    [Theory]
    [InlineData("anonymous", HttpStatusCode.Unauthorized)]
    [InlineData("missing-permission", HttpStatusCode.Forbidden)]
    [InlineData("expired", HttpStatusCode.Unauthorized)]
    [InlineData("wrong-signature", HttpStatusCode.Unauthorized)]
    public async Task Http_UntrustedOrStaleAuthority_DoesNotPersist(string authority, HttpStatusCode expected)
    {
        var original = await fixture.SeedAsync();
        var payload = ProfilePayload();
        payload["PhoneNumber"] = "0690";

        using var response = await PutAsync(original.Id, payload, authority);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(original, await fixture.ReadAsync(original.Id));
    }

    /// <summary>Checks that updating a selected employee leaves another employee unchanged.</summary>
    /// <returns>A task that completes after verifying that updating a selected employee leaves another employee unchanged.</returns>
    [Fact]
    public async Task Http_SelectedEmployeeUpdate_DoesNotChangeAnotherEmployee()
    {
        var original = await fixture.SeedAsync();
        var other = await fixture.SeedAsync();
        var payload = ProfilePayload();
        payload["PhoneNumber"] = "0690";

        using var response = await PutAsync(original.Id, payload);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(other, await fixture.ReadAsync(other.Id));
        AssertManagedFieldsUnchanged(original, await fixture.ReadAsync(original.Id));
    }

    /// <summary>Checks that a valid self-profile update to an absent employee returns not found.</summary>
    /// <returns>A task that completes after verifying that a valid self-profile update to an absent employee returns not found.</returns>
    [Fact]
    public async Task Http_MissingEmployee_ValidProfileReturnsNotFound()
    {
        var payload = ProfilePayload();
        payload["PhoneNumber"] = "0690";
        using var response = await PutAsync(int.MaxValue, payload);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<HttpResponseMessage> PutAsync(int id, Dictionary<string, object?> payload, string authority = "valid")
    {
        using var client = fixture.CreateClient(authority);
        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        return await client.PutAsync($"/employees/{id}/profile", content);
    }

    private static Dictionary<string, object?> ProfilePayload() => new()
    {
        ["FirstName"] = "New",
        ["LastName"] = "Name",
        ["PhoneNumber"] = null,
        ["DateOfBirth"] = null,
    };

    private static void AssertManagedFieldsUnchanged(EmployeeSnapshot original, EmployeeSnapshot persisted)
    {
        Assert.Equal(original.Id, persisted.Id);
        Assert.Equal(original.Email, persisted.Email);
        Assert.Equal(original.RoleId, persisted.RoleId);
        Assert.Equal(original.HomeAddressId, persisted.HomeAddressId);
        Assert.Equal(original.CreatedDate, persisted.CreatedDate);
        Assert.Equal(NullablePhoneFixture.ModifiedDate, persisted.ModifiedDate);
    }
}

/// <summary>Owns disposable PostgreSQL, a controlled-clock production host, and signing keys for nullable-phone tests.</summary>
public sealed class NullablePhoneFixture : IAsyncLifetime
{
    private const string Issuer = "https://nullable-phone.example.invalid";
    private const string Audience = "employee-nullable-phone";
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithName($"employee-null-phone-{Guid.NewGuid():N}").Build();
    private readonly RSA rsa = RSA.Create(2048);
    private readonly RSA otherRsa = RSA.Create(2048);
    /// <summary>Defines the controlled server modification timestamp expected after a self-profile update.</summary>
    public static readonly DateTime ModifiedDate = new(2030, 1, 2, 3, 4, 5, DateTimeKind.Unspecified);
    /// <summary>Gets the production application factory configured with fixture storage and signing trust.</summary>
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    /// <summary>Starts disposable PostgreSQL, applies migrations, and creates the nullable-phone production host.</summary>
    /// <returns>A task that completes after storage migrations and host-factory setup finish.</returns>
    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
        Factory = new NullablePhoneFactory(postgres.GetConnectionString(), rsa);
    }

    /// <summary>Disposes the host, both synthetic signing keys, and the fixture PostgreSQL container.</summary>
    /// <returns>A task that completes after fixture-owned host, key, and database resources are disposed.</returns>
    public async Task DisposeAsync()
    {
        if (Factory is not null) await Factory.DisposeAsync();
        rsa.Dispose();
        otherRsa.Dispose();
        await postgres.DisposeAsync();
    }

    /// <summary>Creates an anonymous or controlled signed client for self-profile and employee-read requests.</summary>
    /// <param name="authority">The client scenario: valid, anonymous, expired, wrong-signature, or missing-permission.</param>
    /// <returns>A caller-owned HTTP client with redirects disabled and the selected synthetic authority.</returns>
    public HttpClient CreateClient(string authority = "valid")
    {
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (authority != "anonymous")
        {
            var now = DateTime.UtcNow;
            var expired = authority == "expired";
            var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, "service:legacy-intranet") };
            if (authority != "missing-permission")
            {
                claims.Add(new Claim("permissions", EmployeePermissions.EmployeesSelfUpdate));
                claims.Add(new Claim("permissions", EmployeePermissions.EmployeesRead));
            }
            var token = new JwtSecurityToken(Issuer, Audience, claims,
                expired ? now.AddHours(-1) : now.AddMinutes(-1),
                expired ? now.AddMinutes(-10) : now.AddMinutes(5),
                new SigningCredentials(new RsaSecurityKey(authority == "wrong-signature" ? otherRsa : rsa), SecurityAlgorithms.RsaSha256));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        }
        return client;
    }

    /// <summary>Persists a synthetic employee with its own role and address and returns the stored comparison snapshot.</summary>
    /// <param name="phone">The initial nullable phone value stored on the synthetic employee.</param>
    /// <returns>A task whose result is the persisted snapshot of the newly seeded employee.</returns>
    public async Task<EmployeeSnapshot> SeedAsync(string? phone = "0404")
    {
        await using var db = CreateDbContext();
        var address = new Address { AddressLine1 = "Owned fixture address", CountryId = 764 };
        var role = new Role { Name = "Owned fixture role" };
        db.AddRange(address, role);
        await db.SaveChangesAsync();
        var date = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var employee = new Employee
        {
            FirstName = "Old",
            LastName = "Name",
            PhoneNumber = phone,
            Email = $"fixture-{Guid.NewGuid():N}@example.invalid",
            DateOfBirth = new DateTime(1990, 1, 1),
            RoleId = role.Id,
            HomeAddressId = address.Id,
            CreatedDate = date,
            ModifiedDate = date,
        };
        db.Add(employee);
        await db.SaveChangesAsync();
        return await ReadAsync(employee.Id);
    }

    /// <summary>Reads the stored fields used to verify narrow profile ownership and managed-field preservation.</summary>
    /// <param name="id">The identifier of the persisted employee whose snapshot is required.</param>
    /// <returns>A task whose result is the matching employee snapshot; an absent identifier causes the single-row query to fail.</returns>
    public async Task<EmployeeSnapshot> ReadAsync(int id)
    {
        await using var db = CreateDbContext();
        return await db.Employees.AsNoTracking().Where(value => value.Id == id)
            .Select(value => new EmployeeSnapshot(value.Id, value.FirstName, value.LastName, value.PhoneNumber,
                value.DateOfBirth, value.Email, value.RoleId, value.HomeAddressId, value.CreatedDate, value.ModifiedDate))
            .SingleAsync();
    }

    private EmployeeDbContext CreateDbContext() => new(new DbContextOptionsBuilder<EmployeeDbContext>()
        .UseNpgsql(postgres.GetConnectionString()).Options);

    private sealed class NullablePhoneFactory(string connectionString, RSA signingKey) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:EmployeeDbContext"] = connectionString,
                ["Jwt:PublicKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(signingKey.ExportSubjectPublicKeyInfoPem())),
                ["Jwt:Issuer"] = Issuer,
                ["Jwt:Audience"] = Audience,
                ["Cache:RedisEnabled"] = "false",
                ["Logging:LogLevel:Default"] = "Warning",
            };
            foreach (var setting in settings) builder.UseSetting(setting.Key, setting.Value);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
            builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(ModifiedDate, TimeSpan.Zero)));
            });
        }
    }
}

/// <summary>Captures stored employee fields for before-and-after comparisons of profile ownership.</summary>
/// <param name="Id">The persisted employee identifier.</param>
/// <param name="FirstName">The stored given name compared after profile updates.</param>
/// <param name="LastName">The stored family name compared after profile updates.</param>
/// <param name="PhoneNumber">The nullable stored phone value whose replacement or clearing is under test.</param>
/// <param name="DateOfBirth">The nullable birth date owned by the self-profile workflow.</param>
/// <param name="Email">The administratively managed email expected to remain unchanged.</param>
/// <param name="RoleId">The nullable role reference expected to remain unchanged.</param>
/// <param name="HomeAddressId">The nullable home-address reference expected to remain unchanged.</param>
/// <param name="CreatedDate">The original server creation timestamp expected to remain unchanged.</param>
/// <param name="ModifiedDate">The server modification timestamp compared with the controlled clock.</param>
public sealed record EmployeeSnapshot(int Id, string FirstName, string LastName, string? PhoneNumber,
    DateTime? DateOfBirth, string Email, int? RoleId, int? HomeAddressId, DateTime? CreatedDate, DateTime? ModifiedDate);

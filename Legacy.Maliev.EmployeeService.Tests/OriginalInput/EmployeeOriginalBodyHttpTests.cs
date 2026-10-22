using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Legacy.Maliev.EmployeeService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.EmployeeService.Tests.OriginalInput;

/// <summary>Serializes original-body HTTP scenarios sharing employee, address, signature, and cache state.</summary>
[CollectionDefinition("Employee original body HTTP", DisableParallelization = true)]
public sealed class EmployeeOriginalBodyHttpCollection;

/// <summary>Checks original-style body grammar through employee, address, and signature HTTP consumers while preserving ignored graph fields and preconditions.</summary>
/// <param name="fixture">The shared route fixture providing the candidate MVC host, PostgreSQL, Redis, signed resource-scoped clients, and controlled IAM transport.</param>
[Collection("Employee original body HTTP")]
public sealed class EmployeeOriginalBodyHttpTests(EmployeeRouteAcceptanceFixture fixture) : IClassFixture<EmployeeRouteAcceptanceFixture>
{
    private const int Selected = 17;
    private const int Sibling = 7;
    private const int ForgedId = 2147483000;
    private static readonly string[] Lanes =
    [
        "employee-create",
        "employee-update",
        "address-create",
        "address-update",
        "signature-update",
        "employee-versioned",
        "address-versioned"
    ];

    /// <summary>Provides write-route and grammar combinations for original-style JSON bodies.</summary>
    /// <returns>Theory rows pairing employee, address, or signature write lanes with supported grammar and UTF-16 variants.</returns>
    public static IEnumerable<object[]> GrammarCases()
    {
        foreach (var lane in Lanes)
        {
            foreach (var grammar in new[] { "single-quotes", "unquoted-names", "comments", "trailing-comma", "second-object", "trailing-garbage", "utf16" })
            {
                yield return [lane, grammar];
            }
        }
    }

    /// <summary>Checks that original-style grammar persists only consumed scalar projections and leaves ignored graph rows unchanged.</summary>
    /// <param name="lane">The employee-create, employee-update, address-create, address-update, signature-update, employee-versioned, or address-versioned HTTP write lane.</param>
    /// <param name="grammar">The single-quote, unquoted-name, comment, trailing-comma, extra-document, trailing-text, or UTF-16 body variant.</param>
    /// <returns>A task that completes after asserting consumed persistence, readback, timestamps, cache effects, and preservation of ignored graph rows and identifiers.</returns>
    [Theory]
    [MemberData(nameof(GrammarCases))]
    public async Task OriginalGrammar_ProjectsOnlyExistingScalarsAndPreservesIgnoredGraph(string lane, string grammar)
    {
        var addressId = await Seed();
        await PrimeEmployees();
        var kind = Kind(lane);
        var selectedId = kind == "address" ? addressId : Selected;
        var employeeBefore = await Table("Employee");
        var addressBefore = await Table("Address");
        var roleBefore = await Table("Role");
        var signatureBefore = await Table("SignatureImageFile");
        var siblingBefore = await EmployeeRow(Sibling);
        var selectedRowBefore = await EmployeeRow(Selected);
        var originalAddressBefore = await Table("Address", addressId);
        var payload = Payload(kind, addressId);
        var body = grammar switch
        {
            "single-quotes" => payload.Replace('"', '\''),
            "unquoted-names" => System.Text.RegularExpressions.Regex.Replace(payload, "\"([A-Za-z][A-Za-z0-9]*)\":", "$1:"),
            "comments" => "/* original body */" + payload.Insert(1, "/* original member */"),
            "trailing-comma" => payload.Insert(payload.Length - 1, ","),
            "second-object" => payload + "{\"Id\":\"ignored-second-document\"}",
            "trailing-garbage" => payload + " ignored-trailing-input",
            "utf16" => payload,
            _ => throw new ArgumentOutOfRangeException(nameof(grammar)),
        };
        using var writer = Writer(lane, selectedId);
        using var content = new StringContent(body, grammar == "utf16" ? Encoding.Unicode : Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(IsCreate(lane) ? HttpMethod.Post : HttpMethod.Put, Route(lane, selectedId))
        {
            Content = content
        };
        if (IsVersioned(lane))
        {
            request.Headers.TryAddWithoutValidation("If-Match", await Version(kind, selectedId));
        }
        var started = DateTime.UtcNow.AddSeconds(-5);
        using var response = await writer.SendAsync(request);
        Assert.Equal(IsCreate(lane) ? HttpStatusCode.Created : HttpStatusCode.NoContent, response.StatusCode);
        var id = selectedId;
        if (IsCreate(lane))
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            id = json.RootElement.GetProperty("Id").GetInt32();
            Assert.True(id > 0);
            Assert.NotEqual(ForgedId, id);
            Assert.NotEqual(selectedId, id);
            Assert.EndsWith("/" + id, response.Headers.Location!.ToString(), StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
        }
        if (IsVersioned(lane))
        {
            Assert.NotNull(response.Headers.ETag);
        }
        await using var db = fixture.CreateContext();
        if (kind == "employee")
        {
            var row = await db.Employees.AsNoTracking().SingleAsync(item => item.Id == id);
            Assert.Equal("ใหม่", row.FirstName);
            Assert.Equal("Employee", row.LastName);
            Assert.Equal("new@example.test", row.Email);
            Assert.Equal("123", row.PhoneNumber);
            Assert.Equal(new DateTime(2001, 2, 3), row.DateOfBirth);
            Assert.Equal(addressId, row.HomeAddressId);
            Assert.Null(row.RoleId);
            Assert.NotEqual("Forged full name", row.FullName);
            Assert.NotNull(row.ModifiedDate);
            Assert.InRange(row.ModifiedDate.Value, started, DateTime.UtcNow.AddMinutes(1));
            if (IsCreate(lane))
            {
                Assert.NotNull(row.CreatedDate);
                Assert.InRange(row.CreatedDate.Value, started, DateTime.UtcNow.AddMinutes(1));
                Assert.Equal(selectedRowBefore, await EmployeeRow(Selected));
            }
            else
            {
                Assert.Equal(new DateTime(2020, 1, 1), row.CreatedDate);
                Assert.Null(await Cache().GetAsync("employee:" + Selected));
            }
            Assert.Equal(siblingBefore, await EmployeeRow(Sibling));
            Assert.Equal(addressBefore, await Table("Address"));
            Assert.Equal(signatureBefore, await Table("SignatureImageFile"));
            Assert.Equal(IsCreate(lane) ? 3 : 2, await db.Employees.CountAsync());
            using var read = ReadClient("employee", id);
            using var detail = JsonDocument.Parse(await read.GetStringAsync("/employees/" + id));
            Assert.Equal("ใหม่", detail.RootElement.GetProperty("FirstName").GetString());
            using var directory = fixture.Client("legacy-employee.employees.list");
            using var list = JsonDocument.Parse(await directory.GetStringAsync("/employees?index=1&size=10"));
            Assert.Contains(list.RootElement.GetProperty("Items").EnumerateArray(), item => item.GetProperty("Id").GetInt32() == id
                && item.GetProperty("FirstName").GetString() == "ใหม่");
        }
        else if (kind == "address")
        {
            var row = await db.Addresses.AsNoTracking().SingleAsync(item => item.Id == id);
            Assert.Equal("B", row.Building);
            Assert.Equal("L1", row.AddressLine1);
            Assert.Equal("L2", row.AddressLine2);
            Assert.Equal("กรุงเทพ", row.City);
            Assert.Equal("S", row.State);
            Assert.Equal("10110", row.PostalCode);
            Assert.Equal(66, row.CountryId);
            Assert.NotNull(row.ModifiedDate);
            Assert.InRange(row.ModifiedDate.Value, started, DateTime.UtcNow.AddMinutes(1));
            if (IsCreate(lane))
            {
                Assert.NotNull(row.CreatedDate);
                Assert.InRange(row.CreatedDate.Value, started, DateTime.UtcNow.AddMinutes(1));
            }
            else
            {
                Assert.Equal(new DateTime(2020, 1, 1), row.CreatedDate);
                Assert.Null(await Cache().GetAsync("employee:" + Selected));
                Assert.Null(await Cache().GetAsync("employee:" + Sibling));
            }
            Assert.Equal(employeeBefore, await Table("Employee"));
            Assert.Equal(signatureBefore, await Table("SignatureImageFile"));
            if (IsCreate(lane))
            {
                Assert.Equal(originalAddressBefore, await Table("Address", addressId));
            }
            Assert.Equal(IsCreate(lane) ? 2 : 1, await db.Addresses.CountAsync());
            using var read = ReadClient("address", id);
            using var detail = JsonDocument.Parse(await read.GetStringAsync("/employees/addresses/" + id));
            Assert.Equal("กรุงเทพ", detail.RootElement.GetProperty("City").GetString());
            if (!IsCreate(lane))
            {
                using var linked = ReadClient("employee", Selected);
                using var employee = JsonDocument.Parse(await linked.GetStringAsync("/employees/" + Selected));
                Assert.Equal("กรุงเทพ", employee.RootElement.GetProperty("HomeAddress").GetProperty("City").GetString());
            }
        }
        else
        {
            var row = await db.SignatureImageFiles.AsNoTracking().SingleAsync();
            Assert.Equal(Selected, row.EmployeeId);
            Assert.Equal("new-bucket", row.Bucket);
            Assert.Equal("new-object", row.ObjectName);
            Assert.NotEqual(ForgedId, row.Id);
            Assert.Equal(new DateTime(2020, 1, 1), row.CreatedDate);
            Assert.NotNull(row.ModifiedDate);
            Assert.InRange(row.ModifiedDate.Value, started, DateTime.UtcNow.AddMinutes(1));
            Assert.Equal(employeeBefore, await Table("Employee"));
            Assert.Equal(addressBefore, await Table("Address"));
            using var read = ReadClient("signature", Selected);
            using var detail = JsonDocument.Parse(await read.GetStringAsync("/employees/signatures/" + Selected));
            Assert.Equal("new-object", detail.RootElement.GetProperty("ObjectName").GetString());
        }
        Assert.Equal(roleBefore, await Table("Role"));
        Assert.False(await db.Employees.AnyAsync(item => item.Id == ForgedId));
        Assert.False(await db.Addresses.AnyAsync(item => item.Id == ForgedId));
        Assert.False(await db.SignatureImageFiles.AnyAsync(item => item.Id == ForgedId));
    }

    /// <summary>Provides invalid nested graph members across employee and address creation, updates, missing rows, and versioned writes.</summary>
    /// <returns>Theory rows identifying the request family, consumer mode, and invalid nested JSON member to prepend.</returns>
    public static IEnumerable<object[]> InvalidGraphs()
    {
        foreach (var mode in new[] { "create", "update", "missing", "versioned", "versioned-missing" })
        {
            yield return ["employee", mode, "\"HomeAddress\":{\"CountryId\":\"private-input-marker\"}"];
            yield return ["employee", mode, "\"Role\":{\"Employee\":[{\"DateOfBirth\":\"private-input-marker\"}]}"];
            yield return ["address", mode, "\"Employee\":[{\"HomeAddress\":{\"CountryId\":\"private-input-marker\"}}]"];
            yield return ["address", mode, "\"Employee\":[{\"Role\":{\"Id\":\"private-input-marker\"}}]"];
        }
    }

    /// <summary>Checks that an invalid earlier graph occurrence is rejected before resource lookup, version preconditions, or persistence.</summary>
    /// <param name="kind">The employee or address request family containing the invalid graph member.</param>
    /// <param name="mode">The create, update, missing-resource, versioned, or versioned-missing consumer scenario.</param>
    /// <param name="invalidMember">The nested JSON member containing an invalid source-shaped scalar that must reject the body before any write.</param>
    /// <returns>A task that completes after asserting sanitized bad-request output and unchanged graph and cache bytes.</returns>
    [Theory]
    [MemberData(nameof(InvalidGraphs))]
    public async Task InvalidEarlierGraph_RejectsBeforeLookupPreconditionOrWrite(string kind, string mode, string invalidMember)
    {
        var addressId = await Seed();
        await PrimeEmployees();
        var before = await Graph();
        var caches = await CacheBytes();
        var lane = kind + "-" + (mode == "missing" ? "update" : mode == "versioned-missing" ? "versioned" : mode);
        var id = mode.Contains("missing", StringComparison.Ordinal) ? int.MaxValue : kind == "employee" ? Selected : addressId;
        using var writer = Writer(lane, id);
        using var content = new StringContent("{" + invalidMember + "," + Payload(kind, addressId)[1..], Encoding.UTF8, "application/json");
        using var response = IsCreate(lane) ? await writer.PostAsync(Route(lane, id), content) : await writer.PutAsync(Route(lane, id), content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("private-input-marker", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(before, await Graph());
        await AssertCacheBytes(caches);
    }

    /// <summary>Checks that omitted, null, empty, and repeated signature owner scalars move route-17 metadata using the source integer default and null-ignore projection.</summary>
    /// <param name="fields">The additional EmployeeId JSON occurrences appended to the signature body, or empty text to omit the owner field.</param>
    /// <param name="owner">The expected projected owner identifier, including zero for an omitted or null-only scalar.</param>
    /// <returns>A task that completes after asserting metadata reassignment, old-route absence, new-route readback, and unchanged employee, address, and role rows.</returns>
    [Theory]
    [InlineData("", 0)]
    [InlineData(",\"EmployeeId\":null", 0)]
    [InlineData(",\"EmployeeId\":\"\"", 0)]
    [InlineData(",\"EmployeeId\":7,\"EmployeeId\":null", 7)]
    [InlineData(",\"EmployeeId\":7,\"EmployeeId\":\"\"", 7)]
    [InlineData(",\"EmployeeId\":0", 0)]
    [InlineData(",\"EmployeeId\":7", 7)]
    public async Task SignatureSourceIntegerDefaultOrNullIgnore_MovesRoute17MetadataAndBothReadbacks(string fields, int owner)
    {
        await Seed();
        var employeesBefore = await Table("Employee");
        var addressesBefore = await Table("Address");
        var rolesBefore = await Table("Role");
        await using var beforeContext = fixture.CreateContext();
        var original = await beforeContext.SignatureImageFiles.AsNoTracking().SingleAsync();
        using var oldRead = ReadClient("signature", Selected);
        using var primed = await oldRead.GetAsync("/employees/signatures/17");
        Assert.Equal(HttpStatusCode.OK, primed.StatusCode);
        using var writer = Writer("signature-update", Selected);
        using var content = new StringContent("{\"Bucket\":\"new-bucket\",\"ObjectName\":\"new-object\"" + fields + "}", Encoding.UTF8, "application/json");
        using var response = await writer.PutAsync("/employees/signatures/17", content);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var db = fixture.CreateContext();
        var row = await db.SignatureImageFiles.AsNoTracking().SingleAsync();
        Assert.Equal(original.Id, row.Id);
        Assert.Equal(original.CreatedDate, row.CreatedDate);
        Assert.Equal(owner, row.EmployeeId);
        Assert.Equal("new-bucket", row.Bucket);
        Assert.Equal("new-object", row.ObjectName);
        using var old = await oldRead.GetAsync("/employees/signatures/17");
        Assert.Equal(HttpStatusCode.NotFound, old.StatusCode);
        using var newRead = ReadClient("signature", owner);
        using var found = await newRead.GetAsync("/employees/signatures/" + owner);
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        using var json = JsonDocument.Parse(await found.Content.ReadAsStringAsync());
        Assert.Equal(owner, json.RootElement.GetProperty("EmployeeId").GetInt32());
        Assert.Equal(original.Id, json.RootElement.GetProperty("Id").GetInt32());
        Assert.Equal("new-object", json.RootElement.GetProperty("ObjectName").GetString());
        Assert.Equal(employeesBefore, await Table("Employee"));
        Assert.Equal(addressesBefore, await Table("Address"));
        Assert.Equal(rolesBefore, await Table("Role"));
    }

    /// <summary>Checks that original-style versioned bodies retain missing-precondition, successful-save, and stale-version outcomes.</summary>
    /// <param name="kind">The employee or address request family used to select its versioned write route and current ETag.</param>
    /// <returns>A task that completes after asserting 428, 204, then 412 responses and preservation of graph and cache state on refused requests.</returns>
    [Theory]
    [InlineData("employee")]
    [InlineData("address")]
    public async Task VersionedConsumer_OriginalBindingRetains428Then204ThenStale412(string kind)
    {
        var addressId = await Seed();
        await PrimeEmployees();
        var id = kind == "employee" ? Selected : addressId;
        var lane = kind + "-versioned";
        var oldVersion = await Version(kind, id);
        var before = await Graph();
        var beforeCache = await CacheBytes();
        using var writer = Writer(lane, id);
        using (var content = new StringContent(Payload(kind, addressId).Replace('"', '\''), Encoding.UTF8, "application/json"))
        using (var missing = await writer.PutAsync(Route(lane, id), content))
        {
            Assert.Equal((HttpStatusCode)428, missing.StatusCode);
        }
        Assert.Equal(before, await Graph());
        await AssertCacheBytes(beforeCache);
        using (var request = new HttpRequestMessage(HttpMethod.Put, Route(lane, id))
        {
            Content = new StringContent(Payload(kind, addressId), Encoding.UTF8, "application/json")
        })
        {
            request.Headers.TryAddWithoutValidation("If-Match", oldVersion);
            using var updated = await writer.SendAsync(request);
            Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
            Assert.NotNull(updated.Headers.ETag);
            Assert.NotEqual(oldVersion, updated.Headers.ETag!.ToString());
        }
        var after = await Graph();
        var afterCache = await CacheBytes();
        using (var request = new HttpRequestMessage(HttpMethod.Put, Route(lane, id))
        {
            Content = new StringContent(Payload(kind, addressId), Encoding.UTF8, "application/json")
        })
        {
            request.Headers.TryAddWithoutValidation("If-Match", oldVersion);
            using var refused = await writer.SendAsync(request);
            Assert.Equal(HttpStatusCode.PreconditionFailed, refused.StatusCode);
        }
        Assert.Equal(after, await Graph());
        await AssertCacheBytes(afterCache);
    }

    /// <summary>Checks that a successfully bound empty object reaches the existing explicit consumer guard without an implicit MVC error dictionary.</summary>
    /// <param name="lane">The employee-create, employee-update, or signature-update consumer receiving an empty JSON object.</param>
    /// <returns>A task that completes after asserting the existing bad-request guard response and unchanged graph and caches.</returns>
    [Theory]
    [InlineData("employee-create")]
    [InlineData("employee-update")]
    [InlineData("signature-update")]
    public async Task SuccessfullyBoundEmptyObject_ReachesExistingExplicitGuardWithoutImplicitMvcErrors(string lane)
    {
        await Seed();
        await PrimeEmployees();
        var before = await Graph();
        var caches = await CacheBytes();
        using var writer = Writer(lane, Selected);
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = IsCreate(lane) ? await writer.PostAsync(Route(lane, Selected), content) : await writer.PutAsync(Route(lane, Selected), content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("\"errors\"", body, StringComparison.Ordinal);
        if (lane == "employee-create")
        {
            Assert.Contains("Employee data is required", body, StringComparison.Ordinal);
        }
        else
        {
            using var problem = JsonDocument.Parse(body);
            Assert.Equal(400, problem.RootElement.GetProperty("status").GetInt32());
            Assert.False(problem.RootElement.TryGetProperty("errors", out _));
        }
        Assert.Equal(before, await Graph());
        await AssertCacheBytes(caches);
    }

    private static string Kind(string lane) => lane.StartsWith("employee", StringComparison.Ordinal) ? "employee"
        : lane.StartsWith("address", StringComparison.Ordinal) ? "address"
        : lane.StartsWith("signature", StringComparison.Ordinal) ? "signature"
        : throw new ArgumentOutOfRangeException(nameof(lane));

    private static bool IsCreate(string lane) => lane.EndsWith("-create", StringComparison.Ordinal);
    private static bool IsVersioned(string lane) => lane.EndsWith("-versioned", StringComparison.Ordinal);
    private static string BaseRoute(string kind) => kind == "employee" ? "/employees"
        : kind == "address" ? "/employees/addresses" : "/employees/signatures";
    private static string Route(string lane, int id) => BaseRoute(Kind(lane)) + (IsCreate(lane) ? "" : "/" + id)
        + (IsVersioned(lane) ? "/versioned" : "");
    private static string Resource(string kind, int id) => kind == "signature" ? "/employees/" + id + "/signature"
        : BaseRoute(kind) + "/" + id;

    private HttpClient Writer(string lane, int id)
    {
        var kind = Kind(lane);
        var permission = kind == "signature" ? "legacy-employee.signatures.write"
            : "legacy-employee." + (kind == "employee" ? "employees" : "addresses") + (IsCreate(lane) ? ".create" : ".update");
        return fixture.Client(permission, IsCreate(lane) ? "global" : Resource(kind, id));
    }

    private HttpClient ReadClient(string kind, int id) => fixture.Client(
        "legacy-employee." + (kind == "employee" ? "employees" : kind == "address" ? "addresses" : "signatures") + ".read", Resource(kind, id));

    private async Task<string> Version(string kind, int id)
    {
        using var reader = ReadClient(kind, id);
        using var response = await reader.GetAsync(BaseRoute(kind) + "/" + id + (kind == "employee" ? "/edit" : ""));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.ETag);
        return response.Headers.ETag!.ToString();
    }

    private async Task PrimeEmployees()
    {
        foreach (var id in new[] { Selected, Sibling })
        {
            using var reader = ReadClient("employee", id);
            using var response = await reader.GetAsync("/employees/" + id);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    private IDistributedCache Cache() => fixture.Factory.Services.GetRequiredService<IDistributedCache>();
    private async Task<byte[]?[]> CacheBytes() => [await Cache().GetAsync("employee:" + Selected), await Cache().GetAsync("employee:" + Sibling)];

    private async Task AssertCacheBytes(byte[]?[] expected)
    {
        var actual = await CacheBytes();
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.Equal(expected[index], actual[index]);
        }
    }

    private static string Payload(string kind, int addressId)
    {
        var date = new DateTime(1980, 1, 1);
        object payload = kind switch
        {
            "employee" => new
            {
                Id = ForgedId,
                FirstName = "ใหม่",
                LastName = "Employee",
                FullName = "Forged full name",
                PhoneNumber = "123",
                Email = "new@example.test",
                DateOfBirth = new DateTime(2001, 2, 3),
                HomeAddressId = addressId,
                RoleId = (int?)null,
                CreatedDate = date,
                ModifiedDate = date,
                HomeAddress = new
                {
                    Id = ForgedId,
                    CountryId = 66,
                    Building = "Ignored nested building"
                },
                Role = new
                {
                    Id = ForgedId,
                    Name = "Ignored role",
                    Description = "Ignored role",
                    Employee = Array.Empty<object>()
                }
            },
            "address" => new
            {
                Id = ForgedId,
                Building = "B",
                AddressLine1 = "L1",
                AddressLine2 = "L2",
                City = "กรุงเทพ",
                State = "S",
                PostalCode = "10110",
                CountryId = 66,
                CreatedDate = date,
                ModifiedDate = date,
                Employee = new[]
                {
                    new
                    {
                        Id = ForgedId,
                        FirstName = "Ignored",
                        LastName = "Graph",
                        Email = "ignored@example.test",
                        HomeAddress = new
                        {
                            CountryId = 66
                        },
                        Role = new
                        {
                            Id = ForgedId
                        }
                    }
                }
            },
            "signature" => new
            {
                Id = ForgedId,
                EmployeeId = Selected,
                Bucket = "new-bucket",
                ObjectName = "new-object",
                CreatedDate = date,
                ModifiedDate = date
            },
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }

    private async Task<int> Seed()
    {
        fixture.Authorities.Clear();
        await using var db = fixture.CreateContext();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"SignatureImageFile\", \"Employee\", \"Address\" RESTART IDENTITY CASCADE");
        var address = new Address
        {
            Building = "Original building",
            City = "Original city",
            CountryId = 66,
            CreatedDate = new DateTime(2020, 1, 1),
            ModifiedDate = new DateTime(2020, 1, 2)
        };
        db.Addresses.Add(address);
        await db.SaveChangesAsync();
        db.Employees.AddRange(new Employee
        {
            Id = Selected,
            FirstName = "Original",
            LastName = "Selected",
            Email = "selected@example.test",
            HomeAddressId = address.Id,
            CreatedDate = new DateTime(2020, 1, 1),
            ModifiedDate = new DateTime(2020, 1, 2)
        }, new Employee
        {
            Id = Sibling,
            FirstName = "Original",
            LastName = "Sibling",
            Email = "sibling@example.test",
            HomeAddressId = address.Id,
            CreatedDate = new DateTime(2020, 1, 1),
            ModifiedDate = new DateTime(2020, 1, 2)
        });
        db.SignatureImageFiles.Add(new SignatureImageFile
        {
            EmployeeId = Selected,
            Bucket = "original-bucket",
            ObjectName = "original-object",
            CreatedDate = new DateTime(2020, 1, 1),
            ModifiedDate = new DateTime(2020, 1, 2)
        });
        await db.SaveChangesAsync();
        await Cache().RemoveAsync("employee:" + Selected);
        await Cache().RemoveAsync("employee:" + Sibling);
        return address.Id;
    }

    private Task<string> EmployeeRow(int id) => Table("Employee", id);

    private async Task<string> Table(string table, int? id = null)
    {
        if (table is not ("Employee" or "Address" or "Role" or "SignatureImageFile"))
        {
            throw new ArgumentOutOfRangeException(nameof(table));
        }
        await using var db = fixture.CreateContext();
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT COALESCE(jsonb_agg(to_jsonb(t) || jsonb_build_object('_xmin', t.xmin::text) ORDER BY t.\"ID\"), '[]'::jsonb)::text FROM \"" + table + "\" t";
            if (id.HasValue)
            {
                command.CommandText += " WHERE t.\"ID\" = @id";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "id";
                parameter.Value = id.Value;
                command.Parameters.Add(parameter);
            }
            return (string)(await command.ExecuteScalarAsync())!;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private async Task<string> Graph() => JsonSerializer.Serialize(new
    {
        Employees = await Table("Employee"),
        Addresses = await Table("Address"),
        Roles = await Table("Role"),
        Signatures = await Table("SignatureImageFile")
    });
}

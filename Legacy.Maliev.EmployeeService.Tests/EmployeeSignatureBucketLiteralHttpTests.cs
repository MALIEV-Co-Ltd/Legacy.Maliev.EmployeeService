using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.EmployeeService.Domain;
using Legacy.Maliev.EmployeeService.Tests.OriginalInput;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.EmployeeService.Tests;

[Collection("Employee signature persistence")]
public sealed class EmployeeSignatureBucketLiteralHttpTests(EmployeeRouteAcceptanceFixture fixture)
    : IClassFixture<EmployeeRouteAcceptanceFixture>
{
    [Theory]
    [InlineData(false, "  synthetic-bucket  ")]
    [InlineData(true, "  synthetic-bucket  ")]
    [InlineData(false, "\tsynthetic-bucket\t")]
    [InlineData(true, "\tsynthetic-bucket\t")]
    [InlineData(false, "\u00a0ถังลายเซ็น\u00a0")]
    [InlineData(true, "\u00a0ถังลายเซ็น\u00a0")]
    [InlineData(false, "\u2003synthetic-bucket\u2003")]
    [InlineData(true, "\u2003synthetic-bucket\u2003")]
    public async Task SignificantBucketPadding_PostAndPutPreserveExactMetadata(bool update, string bucket)
    {
        await SeedAsync(update);
        var before = await SnapshotAsync(update ? 101 : null);
        var beforeWrite = DateTime.UtcNow;
        const string objectName = "  signatures/ลายเซ็น +%?.png  ";
        using var writer = Writer(update);
        using var response = await SendAsync(writer, update, bucket, objectName);
        var afterWrite = DateTime.UtcNow;
        Assert.Equal(update ? HttpStatusCode.NoContent : HttpStatusCode.Created, response.StatusCode);
        await using var db = fixture.CreateContext();
        var saved = await db.SignatureImageFiles.AsNoTracking().SingleAsync(row => row.EmployeeId == 17);
        Assert.Equal(bucket, saved.Bucket);
        Assert.Equal(objectName, saved.ObjectName);
        Assert.NotNull(saved.ModifiedDate);
        Assert.InRange(saved.ModifiedDate.Value, beforeWrite.AddSeconds(-1), afterWrite.AddSeconds(1));
        Assert.Equal(before, await SnapshotAsync(saved.Id));
        if (update)
        {
            Assert.Equal(101, saved.Id);
            Assert.Equal(new DateTime(2020, 1, 1), saved.CreatedDate);
        }
        else
        {
            Assert.NotNull(saved.CreatedDate);
            Assert.InRange(saved.CreatedDate.Value, beforeWrite.AddSeconds(-1), afterWrite.AddSeconds(1));
            Assert.EndsWith("/employees/signatures/17", response.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
            using var acknowledgement = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(saved.Id, acknowledgement.RootElement.GetProperty("Id").GetInt32());
            Assert.Equal(bucket, acknowledgement.RootElement.GetProperty("Bucket").GetString());
            Assert.Equal(objectName, acknowledgement.RootElement.GetProperty("ObjectName").GetString());
        }
        using var reader = fixture.Client("legacy-employee.signatures.read", "/employees/17/signature");
        using var read = await reader.GetAsync("/employees/signatures/17/");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var json = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.Equal(saved.Id, json.RootElement.GetProperty("Id").GetInt32());
        Assert.Equal(17, json.RootElement.GetProperty("EmployeeId").GetInt32());
        Assert.Equal(bucket, json.RootElement.GetProperty("Bucket").GetString());
        Assert.Equal(objectName, json.RootElement.GetProperty("ObjectName").GetString());
        Assert.False(json.RootElement.TryGetProperty("bucket", out _));
        Assert.False(json.RootElement.TryGetProperty("objectName", out _));
        fixture.IamTransportEvidence.AssertHealthy();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PaddedBucketLimit_StoresAllFiftyAndRejectsFiftyOneWithoutTrimming(bool update)
    {
        await SeedAsync(update);
        using var writer = Writer(update);
        var bucket = "\t " + new string('x', 46) + " \t";
        Assert.Equal(50, bucket.Length);
        using var accepted = await SendAsync(writer, update, bucket, "accepted-synthetic.png");
        Assert.Equal(update ? HttpStatusCode.NoContent : HttpStatusCode.Created, accepted.StatusCode);
        await using var db = fixture.CreateContext();
        Assert.Equal(bucket, (await db.SignatureImageFiles.AsNoTracking().SingleAsync(row => row.EmployeeId == 17)).Bucket);
        var before = await SnapshotAsync();
        var oversized = "\t " + new string('x', 47) + " \t";
        Assert.Equal(51, oversized.Length);
        using var rejected = await SendAsync(writer, update, oversized, "rejected-private-synthetic.png");
        Assert.Equal(HttpStatusCode.InternalServerError, rejected.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
        using var error = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        Assert.Equal(500, error.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal(JsonValueKind.Null, error.RootElement.GetProperty("details").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(error.RootElement.GetProperty("traceId").GetString()));
        var body = await rejected.Content.ReadAsStringAsync();
        foreach (var marker in new[] { new string('x', 47), "rejected-private-synthetic", "Npgsql", "SignatureImageFile" })
            Assert.DoesNotContain(marker, body, StringComparison.OrdinalIgnoreCase);
        fixture.IamTransportEvidence.AssertHealthy();
    }

    [Theory]
    [InlineData(false, "anonymous", HttpStatusCode.Unauthorized)]
    [InlineData(true, "anonymous", HttpStatusCode.Unauthorized)]
    [InlineData(false, "missing-permission", HttpStatusCode.Forbidden)]
    [InlineData(true, "missing-permission", HttpStatusCode.Forbidden)]
    public async Task PaddedBucketWrites_KeepExistingAuthorityAndLeaveWholeGraphUnchanged(
        bool update, string authority, HttpStatusCode expected)
    {
        await SeedAsync(update);
        var before = await SnapshotAsync();
        using var writer = fixture.Client("legacy-employee.signatures.write",
            update ? "/employees/17/signature" : "/employees/17", authority: authority);
        using var response = await SendAsync(writer, update, "  private-synthetic-bucket  ", "private-synthetic-object.png");
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
        fixture.IamTransportEvidence.AssertHealthy();
    }

    public static IEnumerable<object[]> WhitespaceOnlyMetadataCases()
    {
        foreach (var field in new[] { "Bucket", "ObjectName" })
        {
            foreach (var value in new[] { " ", "\t", "\u00a0", "\u2003" })
            {
                foreach (var wire in new[] { "json", "utf16" })
                    yield return [field, value, wire];
            }
            foreach (var wire in new[] { "text-json", "vendor-json", "single-quotes", "unquoted-names", "comments", "trailing-comma", "second-object", "trailing-garbage", "duplicate-null" })
                yield return [field, " \t\u00a0\u2003", wire];
        }
    }

    [Theory]
    [MemberData(nameof(WhitespaceOnlyMetadataCases))]
    public async Task WhitespaceOnlyMetadata_PutMatchesSourceReaderAndPreservesLiteralStorage(
        string field, string value, string wire)
    {
        var bucket = field == "Bucket" ? value : "source-bucket";
        var objectName = field == "ObjectName" ? value : "source-object.png";
        var payload = JsonSerializer.Serialize(new { Bucket = bucket, ObjectName = objectName, EmployeeId = 17 });
        var body = wire switch
        {
            "single-quotes" => payload.Replace('"', '\''),
            "unquoted-names" => System.Text.RegularExpressions.Regex.Replace(payload, "\"([A-Za-z][A-Za-z0-9]*)\":", "$1:"),
            "comments" => "/* source body */" + payload.Insert(1, "/* source member */"),
            "trailing-comma" => payload.Insert(payload.Length - 1, ","),
            "second-object" => payload + "{\"Bucket\":\"ignored-second-document\"}",
            "trailing-garbage" => payload + " ignored-trailing-input",
            "duplicate-null" => payload.Insert(payload.Length - 1, ",\"" + field + "\":null"),
            "json" or "utf16" or "text-json" or "vendor-json" => payload,
            _ => throw new ArgumentOutOfRangeException(nameof(wire)),
        };
        var encoding = wire == "utf16" ? Encoding.Unicode : Encoding.UTF8;
        var mediaType = wire == "text-json" ? "text/json"
            : wire == "vendor-json" ? "application/vnd.maliev+json" : "application/json";
        using var content = new StringContent(body, encoding, mediaType);
        var source = await EmployeeOriginalInputOracle.ReadBytesAsync(
            EmployeeOriginalInputOracle.Formatter(), EmployeeOriginalInputOracle.SourceType("signature"),
            new EmptyModelMetadataProvider(), encoding.GetBytes(body), content.Headers.ContentType!.ToString());
        Assert.Equal("success", source.Kind);
        var original = Assert.IsType<SourceSignature>(source.Model);
        Assert.Equal(bucket, original.Bucket);
        Assert.Equal(objectName, original.ObjectName);
        Assert.Equal(17, original.EmployeeId);

        await SeedAsync(true);
        var unrelated = await SnapshotAsync(101);
        using var writer = Writer(true);
        var started = DateTime.UtcNow;
        using var response = await writer.PutAsync("/employees/signatures/17", content);
        var finished = DateTime.UtcNow;
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
        await using (var db = fixture.CreateContext())
        {
            var saved = await db.SignatureImageFiles.AsNoTracking().SingleAsync(row => row.Id == 101);
            Assert.Equal(17, saved.EmployeeId);
            Assert.Equal(bucket, saved.Bucket);
            Assert.Equal(objectName, saved.ObjectName);
            Assert.Equal(new DateTime(2020, 1, 1), saved.CreatedDate);
            Assert.NotNull(saved.ModifiedDate);
            Assert.InRange(saved.ModifiedDate.Value, started.AddSeconds(-1), finished.AddSeconds(1));
        }
        using var reader = fixture.Client("legacy-employee.signatures.read", "/employees/17/signature");
        using var read = await reader.GetAsync("/employees/signatures/17/");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var json = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.Equal(101, json.RootElement.GetProperty("Id").GetInt32());
        Assert.Equal(17, json.RootElement.GetProperty("EmployeeId").GetInt32());
        Assert.Equal(bucket, json.RootElement.GetProperty("Bucket").GetString());
        Assert.Equal(objectName, json.RootElement.GetProperty("ObjectName").GetString());
        Assert.False(json.RootElement.TryGetProperty("bucket", out _));
        Assert.False(json.RootElement.TryGetProperty("objectName", out _));
        Assert.Equal(unrelated, await SnapshotAsync(101));
        fixture.IamTransportEvidence.AssertHealthy();
    }

    [Theory]
    [InlineData("Bucket", " ")]
    [InlineData("ObjectName", " ")]
    [InlineData("Bucket", "\t")]
    [InlineData("ObjectName", "\t")]
    [InlineData("Bucket", "\u00a0")]
    [InlineData("ObjectName", "\u00a0")]
    [InlineData("Bucket", "\u2003")]
    [InlineData("ObjectName", "\u2003")]
    public async Task WhitespaceOnlyQuery_PostRetainsMvcRejectionWithoutWrites(string field, string value)
    {
        await SeedAsync(false);
        var before = await SnapshotAsync();
        using var writer = Writer(false);
        using var response = await SendAsync(writer, false,
            field == "Bucket" ? value : "synthetic-bucket",
            field == "ObjectName" ? value : "synthetic.png");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
        fixture.IamTransportEvidence.AssertHealthy();
    }

    [Theory]
    [InlineData("anonymous", HttpStatusCode.Unauthorized)]
    [InlineData("wrong-signature", HttpStatusCode.Unauthorized)]
    [InlineData("missing-permission", HttpStatusCode.Forbidden)]
    public async Task WhitespaceOnlyMetadata_PutRetainsAuthorityBeforeWrites(string authority, HttpStatusCode expected)
    {
        await SeedAsync(true);
        var before = await SnapshotAsync();
        using var writer = fixture.Client("legacy-employee.signatures.write", "/employees/17/signature", authority: authority);
        using var response = await writer.PutAsJsonAsync("/employees/signatures/17",
            new { Bucket = " ", ObjectName = "\t", EmployeeId = 17 });
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
        fixture.IamTransportEvidence.AssertHealthy();
    }

    private HttpClient Writer(bool update) => fixture.Client("legacy-employee.signatures.write",
        update ? "/employees/17/signature" : "/employees/17");

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, bool update, string bucket, string objectName) => update
        ? client.PutAsJsonAsync("/employees/signatures/17", new { Bucket = bucket, ObjectName = objectName, EmployeeId = 17 })
        : client.PostAsync($"/employees/17/signatures?bucket={Uri.EscapeDataString(bucket)}&objectName={Uri.EscapeDataString(objectName)}", null);

    private async Task SeedAsync(bool signature)
    {
        await using var db = fixture.CreateContext();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"Employee\", \"Address\", \"Role\", \"SignatureImageFile\" RESTART IDENTITY CASCADE");
        db.Roles.Add(new Role { Id = 21, Name = "ช่าง", Description = "synthetic-role", CreatedDate = new DateTime(2020, 1, 1) });
        db.Addresses.Add(new Address { Id = 31, AddressLine1 = "synthetic-address", CountryId = 66, CreatedDate = new DateTime(2020, 1, 1) });
        db.Employees.AddRange(
            new Employee { Id = 17, RoleId = 21, HomeAddressId = 31, FirstName = "Owner", LastName = "Synthetic", Email = "owner@example.invalid" },
            new Employee { Id = 18, RoleId = 21, HomeAddressId = 31, FirstName = "Other", LastName = "Synthetic", Email = "other@example.invalid" });
        if (signature) db.SignatureImageFiles.Add(new SignatureImageFile
        {
            Id = 101,
            EmployeeId = 17,
            Bucket = "original",
            ObjectName = "original.png",
            CreatedDate = new DateTime(2020, 1, 1),
            ModifiedDate = new DateTime(2020, 1, 2)
        });
        db.SignatureImageFiles.Add(new SignatureImageFile
        {
            Id = 301,
            EmployeeId = 18,
            Bucket = "unrelated",
            ObjectName = "unrelated.png",
            CreatedDate = new DateTime(2020, 1, 1),
            ModifiedDate = new DateTime(2020, 1, 2)
        });
        await db.SaveChangesAsync();
        fixture.Authorities.Clear();
    }

    private async Task<string> SnapshotAsync(int? excludedSignatureId = null)
    {
        await using var db = fixture.CreateContext();
        // Include PostgreSQL row versions to detect even a write that restores the same values.
        return await db.Database.SqlQuery<string>($"""
            SELECT json_build_object(
                'Employees', (SELECT json_agg(json_build_array(row_to_json(t), t.xmin::text) ORDER BY t."ID") FROM "Employee" t),
                'Addresses', (SELECT json_agg(json_build_array(row_to_json(t), t.xmin::text) ORDER BY t."ID") FROM "Address" t),
                'Roles', (SELECT json_agg(json_build_array(row_to_json(t), t.xmin::text) ORDER BY t."ID") FROM "Role" t),
                'Signatures', (SELECT json_agg(json_build_array(row_to_json(t), t.xmin::text) ORDER BY t."ID") FROM "SignatureImageFile" t WHERE t."ID" <> {excludedSignatureId ?? -1})
            )::text AS "Value"
            """).SingleAsync();
    }
}

using System.Net;
using System.Text.Json;

namespace Legacy.Maliev.EmployeeService.Tests;

public sealed class EmployeeDirectoryParityHttpTests(EmployeeDirectoryParityFixture fixture)
    : IClassFixture<EmployeeDirectoryParityFixture>
{
    [Theory]
    [InlineData("LastName")]
    [InlineData("Email")]
    [InlineData("PhoneNumber")]
    public async Task SourceSearch_RemainingStoredTextFieldsMatchLiteralInputs(string field)
    {
        (string Literal, string Distractor)[] inputs =
        [
            ("Sales%Lead", "SalesXLead"),
            ("Code_1", "CodeA1"),
            ("Back\\Slash", "BackSlash"),
            ("วิศวกร", "Engineer"),
            ("  Engineer  ", "Engineer"),
        ];
        foreach (var (literal, distractor) in inputs)
        {
            await fixture.SeedAsync(StoredFieldRow(1, field, literal),
                StoredFieldRow(2, field, distractor),
                new(3, "Unrelated", "Employee", "unrelated@example.invalid"));
            await AssertPageAsync($"search={Uri.EscapeDataString(literal)}", [1], 1, 1, 1);
        }
    }

    [Fact]
    public async Task SourceSearch_ComputedFullNameMatchesAcrossNameBoundary()
    {
        await fixture.SeedAsync(new(1, "Given", "Family", "target@example.invalid"),
            new(2, "Given", "Other", "other@example.invalid"),
            new(3, "Other", "Family", "third@example.invalid"));
        await AssertPageAsync($"search={Uri.EscapeDataString("Given Family")}", [1], 1, 1, 1);
    }

    private static DirectoryRow StoredFieldRow(int id, string field, string value) => field switch
    {
        "LastName" => new(id, "Given", value, $"row-{id}@example.invalid"),
        "Email" => new(id, "Given", "Family", value),
        "PhoneNumber" => new(id, "Given", "Family", $"row-{id}@example.invalid", value),
        _ => throw new ArgumentOutOfRangeException(nameof(field)),
    };

    [Theory]
    [InlineData("7")]
    [InlineData("007")]
    public async Task NumericSearch_SelectsOnlyExactEmployeeId(string search)
    {
        await fixture.SeedAsync(new(7, "Exact", "Employee", "exact@example.invalid"),
            new(8, "Name7", "Other", "other@example.invalid"),
            new(9, "Phone", "Other", "phone@example.invalid", "0707"),
            new(10, "Email", "Other", "email7@example.invalid"));
        await AssertPageAsync($"search={search}", [7], 1, 1, 1);
    }

    [Fact]
    public async Task AbsentNumericId_DoesNotMatchPhoneOrEmail()
    {
        await fixture.SeedAsync(new DirectoryRow(1, "Other", "Employee", "7000@example.invalid", "7000"));
        await AssertNotFoundAsync("search=7000");
    }

    [Theory]
    [InlineData("Sales%Lead", "SalesXLead")]
    [InlineData("Code_1", "CodeA1")]
    [InlineData("Back\\Slash", "BackSlash")]
    [InlineData("%_\\", "XYZ")]
    public async Task SearchSpecialCharacters_AreLiteralSubstrings(string literal, string distractor)
    {
        await fixture.SeedAsync(new(1, literal, "Target", "target@example.invalid"),
            new(2, distractor, "Other", "other@example.invalid"));
        await AssertPageAsync($"search={Uri.EscapeDataString(literal)}", [1], 1, 1, 1);
    }

    [Fact]
    public async Task EmptySelectedPage_ReturnsSourceNotFound()
    {
        await fixture.SeedAsync(new(1, "Engineer", "One", "one@example.invalid"),
            new(2, "Engineer", "Two", "two@example.invalid"));
        await AssertNotFoundAsync("search=Engineer&index=3&size=1");
    }

    [Fact]
    public async Task LiteralFilteredPaging_CountsOnlyLiteralMatches()
    {
        await fixture.SeedAsync(new(1, "Sales%Lead", "One", "one@example.invalid"),
            new(2, "More%Work", "Two", "two@example.invalid"),
            new(3, "Ordinary", "Three", "three@example.invalid"));
        await AssertPageAsync("search=%25&index=2&size=1", [2], 2, 2, 2);
    }

    [Theory]
    [InlineData("ENg", 1)]
    [InlineData("วิศว", 2)]
    [InlineData("  Engineer  ", 1)]
    public async Task TextSearch_PreservesEnglishThaiAndSignificantPadding(string search, int id)
    {
        var firstName = search == "  Engineer  " ? search : "Engineer";
        await fixture.SeedAsync(new(1, firstName, "One", "one@example.invalid"),
            new(2, "วิศวกร", "ไทย", "two@example.invalid"));
        await AssertPageAsync($"search={Uri.EscapeDataString(search)}", [id], 1, 1, 1);
    }

    [Theory]
    [InlineData("EmployeeId_Ascending", 1, 2, 3)]
    [InlineData("EmployeeId_Descending", 3, 2, 1)]
    [InlineData("EmployeeEmail_Ascending", 2, 3, 1)]
    [InlineData("EmployeeEmail_Descending", 1, 3, 2)]
    public async Task AllSourceSortFlags_PreserveWireOrder(string sort, int first, int second, int third)
    {
        await fixture.SeedAsync(new(1, "One", "Employee", "z@example.invalid"),
            new(2, "Two", "Employee", "a@example.invalid"), new(3, "Three", "Employee", "m@example.invalid"));
        await AssertPageAsync($"sort={sort}", [first, second, third], 1, 1, 3);
    }

    [Theory]
    [InlineData("", 50, 6)]
    [InlineData("size=10000", 250, 2)]
    [InlineData("index=0&size=0", 1, 260)]
    public async Task CurrentBoundedDefaultsAndClamps_AreRetained(string query, int count, int pages)
    {
        await fixture.SeedAsync(Enumerable.Range(1, 260)
            .Select(id => new DirectoryRow(id, "Bulk", "Employee", $"bulk-{id:D3}@example.invalid")).ToArray());
        await AssertPageAsync(query, Enumerable.Range(1, count).ToArray(), 1, pages, 260);
    }

    [Fact]
    public async Task Projection_PreservesPascalCaseNestedAddressAndNullOmission()
    {
        await fixture.SeedAsync(new DirectoryRow(1, "Thai", "Engineer", "thai@example.invalid", Address: true));
        var snapshot = await fixture.SnapshotAsync();
        using var client = fixture.CreateClient();
        using var response = await client.GetAsync("/employees");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = Assert.Single(json.RootElement.GetProperty("Items").EnumerateArray());
        Assert.Equal("Thai Engineer", item.GetProperty("FullName").GetString());
        Assert.Equal("Thai fixture road", item.GetProperty("HomeAddress").GetProperty("AddressLine1").GetString());
        Assert.Equal(764, item.GetProperty("HomeAddress").GetProperty("CountryId").GetInt32());
        Assert.False(item.TryGetProperty("PhoneNumber", out _));
        Assert.False(item.TryGetProperty("Role", out _));
        Assert.False(item.TryGetProperty("firstName", out _));
        Assert.False(item.TryGetProperty("PasswordHash", out _));
        Assert.Equal(snapshot, await fixture.SnapshotAsync());
    }

    [Theory]
    [InlineData("anonymous", HttpStatusCode.Unauthorized)]
    [InlineData("missing-permission", HttpStatusCode.Forbidden)]
    [InlineData("wrong-signature", HttpStatusCode.Unauthorized)]
    [InlineData("expired", HttpStatusCode.Unauthorized)]
    public async Task NormalSignedAuthority_DenialsLeaveDatabaseUnchanged(string authority, HttpStatusCode expected)
    {
        await fixture.SeedAsync(new DirectoryRow(1, "Private", "Employee", "private@example.invalid"));
        var snapshot = await fixture.SnapshotAsync();
        using var client = fixture.CreateClient(authority);
        using var response = await client.GetAsync("/employees");
        Assert.Equal(expected, response.StatusCode);
        Assert.DoesNotContain("private@example.invalid", await response.Content.ReadAsStringAsync());
        Assert.Equal(snapshot, await fixture.SnapshotAsync());
    }

    [Fact]
    public async Task CallerCancellation_ReachesActualDirectoryQueryAndLeavesDatabaseUnchanged()
    {
        await fixture.SeedAsync(new DirectoryRow(1, "Stored", "Employee", "stored@example.invalid"));
        var snapshot = await fixture.SnapshotAsync();
        using var client = fixture.CreateClient();
        using var cancellation = new CancellationTokenSource();
        fixture.CancellationGate.Arm();
        var pending = client.GetAsync("/employees", cancellation.Token);
        await fixture.CancellationGate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        await fixture.CancellationGate.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(snapshot, await fixture.SnapshotAsync());
        await AssertPageAsync("", [1], 1, 1, 1);
    }

    [Fact]
    public async Task MissingText_ReturnsNotFoundWithoutMutation()
    {
        await fixture.SeedAsync(new DirectoryRow(1, "Stored", "Employee", "stored@example.invalid"));
        await AssertNotFoundAsync("search=not-present");
    }

    private async Task AssertPageAsync(string query, int[] ids, int index, int pages, int total)
    {
        var snapshot = await fixture.SnapshotAsync();
        using var client = fixture.CreateClient();
        using var response = await client.GetAsync($"/employees?{query}");
        Assert.Equal(snapshot, await fixture.SnapshotAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(ids, root.GetProperty("Items").EnumerateArray().Select(value => value.GetProperty("Id").GetInt32()).ToArray());
        Assert.Equal(index, root.GetProperty("PageIndex").GetInt32());
        Assert.Equal(pages, root.GetProperty("TotalPages").GetInt32());
        Assert.Equal(total, root.GetProperty("TotalRecords").GetInt32());
        Assert.Equal(index < pages, root.GetProperty("HasNextPage").GetBoolean());
        Assert.Equal(index > 1, root.GetProperty("HasPreviousPage").GetBoolean());
    }

    private async Task AssertNotFoundAsync(string query)
    {
        var snapshot = await fixture.SnapshotAsync();
        using var client = fixture.CreateClient();
        using var response = await client.GetAsync($"/employees?{query}");
        Assert.Equal(snapshot, await fixture.SnapshotAsync());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

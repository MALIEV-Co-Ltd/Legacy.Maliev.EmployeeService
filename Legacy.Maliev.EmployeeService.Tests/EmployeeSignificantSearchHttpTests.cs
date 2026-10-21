using System.Net;
using System.Text.Json;

namespace Legacy.Maliev.EmployeeService.Tests;

/// <summary>Checks significant directory search text, numeric identifier interpretation, and null or whitespace query binding.</summary>
/// <param name="fixture">The directory parity fixture providing seeded employee rows, signed clients, and mutation snapshots.</param>
public sealed class EmployeeSignificantSearchHttpTests(EmployeeDirectoryParityFixture fixture)
    : IClassFixture<EmployeeDirectoryParityFixture>
{
    /// <summary>Checks that significant padded text and wildcard-like characters match the original literal rather than a trimmed distractor.</summary>
    /// <param name="search">The literal padded or wildcard-like search text stored on the target employee and sent in the query.</param>
    /// <returns>A task that completes after asserting only the literal target is returned and storage remains unchanged.</returns>
    [Theory]
    [InlineData("  Engineer  ")]
    [InlineData("\tEngineer\t")]
    [InlineData(" Engineer ")]
    [InlineData(" % ")]
    [InlineData(" _ ")]
    [InlineData(" \\ ")]
    public async Task SignificantText_MatchesOnlyTheOriginalLiteral(string search)
    {
        var distractor = string.IsNullOrWhiteSpace(search) ? "Engineer" : search.Trim();
        await fixture.SeedAsync(new(1, search, "Target", "target@example.invalid"),
            new(2, distractor, "Other", "other@example.invalid"));
        await AssertResultAsync(search, [1]);
    }

    /// <summary>Checks that signed or padded integer search text retains exact employee identifier matching.</summary>
    /// <param name="search">The signed or whitespace-padded integer text expected to resolve to employee identifier 7.</param>
    /// <returns>A task that completes after asserting the exact identifier result and unchanged storage.</returns>
    [Theory]
    [InlineData(" 007 ")]
    [InlineData("+7")]
    [InlineData(" 7\t")]
    public async Task NumericWhitespaceAndSign_RetainExactIdInterpretation(string search)
    {
        await fixture.SeedAsync(new(7, "Exact", "Employee", "exact@example.invalid"),
            new(8, "Name7", "Other", "other@example.invalid", "0707"));
        await AssertResultAsync(search, [7]);
    }

    /// <summary>Checks that absent significant padded text returns not found instead of exposing an unfiltered directory.</summary>
    /// <param name="search">The nonblank padded search text absent from the seeded employee values.</param>
    /// <returns>A task that completes after asserting not found and unchanged directory storage.</returns>
    [Theory]
    [InlineData(" Engineer ")]
    [InlineData("\tEngineer\t")]
    [InlineData(" \tMissing\t ")]
    public async Task AbsentSignificantText_IsNotAnUnfilteredDirectory(string search)
    {
        await fixture.SeedAsync(new DirectoryRow(1, "Engineer", "One", "one@example.invalid"));
        await AssertResultAsync(search, []);
    }

    /// <summary>Checks that MVC binding of null, empty, or whitespace-only search values retains the unfiltered directory.</summary>
    /// <param name="search">The omitted, empty, or whitespace-only query value expected to bind as an unfiltered search.</param>
    /// <returns>A task that completes after asserting all seeded employees and unchanged storage.</returns>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("\t")]
    [InlineData(" \t ")]
    public async Task DefaultMvcNullEmptyOrWhitespaceBinding_RetainsUnfilteredDirectory(string? search)
    {
        await fixture.SeedAsync(new(1, "Engineer", "One", "one@example.invalid"),
            new(2, "Stored", "Two", "two@example.invalid"));
        await AssertResultAsync(search, [1, 2]);
    }

    private async Task AssertResultAsync(string? search, int[] expectedIds)
    {
        var before = await fixture.SnapshotAsync();
        using var client = fixture.CreateClient();
        var query = search is null ? "" : $"?search={Uri.EscapeDataString(search)}";
        using var response = await client.GetAsync($"/employees{query}");
        Assert.Equal(before, await fixture.SnapshotAsync());
        if (expectedIds.Length == 0)
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            return;
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(expectedIds, root.GetProperty("Items").EnumerateArray()
            .Select(item => item.GetProperty("Id").GetInt32()).ToArray());
        Assert.Equal(expectedIds.Length, root.GetProperty("TotalRecords").GetInt32());
        Assert.Equal(1, root.GetProperty("PageIndex").GetInt32());
        Assert.Equal(1, root.GetProperty("TotalPages").GetInt32());
        Assert.False(root.GetProperty("HasNextPage").GetBoolean());
        Assert.False(root.GetProperty("HasPreviousPage").GetBoolean());
    }
}

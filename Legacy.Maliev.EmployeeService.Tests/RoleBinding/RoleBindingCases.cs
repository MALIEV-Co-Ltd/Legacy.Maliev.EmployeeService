namespace Legacy.Maliev.EmployeeService.Tests.RoleBinding;

// Literal expectations are independent of both readers. No original binary/provider claim.
/// <summary>Provides independent literal role text expectations and request bodies without claiming historical binary or provider equivalence.</summary>
public static class RoleBindingCases
{
    private static readonly string[] Fields = ["Name", "Description"];
    private static readonly string[] ValidIds =
        ["replace", "null_after", "null_before", "empty_after", "repeat_null", "all_null",
         "omitted", "padded", "integer", "decimal", "true", "false"];

    /// <summary>Combines role Name and Description with accepted scalar and duplicate-order case identifiers.</summary>
    public static IEnumerable<object[]> Valid => Fields.SelectMany(member =>
        ValidIds.Select(id => new object[] { member, id }));

    /// <summary>Combines role text properties with invalid object or array values placed alone, before, or after text.</summary>
    public static IEnumerable<object[]> Malformed => Fields.SelectMany(member =>
        new[] { "object", "array" }.SelectMany(token =>
            new[] { "only", "before", "after" }.Select(position => new object[] { member, token, position })));

    /// <summary>Provides role create/update cases for retained duplicate-null text and Boolean conversion.</summary>
    public static IEnumerable<object[]> HttpValid => Fields.SelectMany(member =>
        new[] { "null_after", "true" }.SelectMany(id =>
            new[] { false, true }.Select(update => new object[] { member, id, update })));

    /// <summary>Provides role create/update cases for whole-null bodies and earlier malformed duplicate values.</summary>
    public static IEnumerable<object[]> HttpRejected => new[] { false, true }.SelectMany(update =>
        new[] { "null", "{\"Name\":{},\"Name\":\"last\"}", "{\"Description\":[],\"Description\":\"last\"}" }
            .Select(json => new object[] { update, json }));

    /// <summary>Combines role create/update operations with anonymous, bad-signature, expired, and missing-permission actors.</summary>
    public static IEnumerable<object[]> Denied => new[] { false, true }.SelectMany(update =>
        new[] { "anonymous", "wrong-signature", "expired", "missing-permission" }
            .Select(authority => new object[] { update, authority }));

    /// <summary>Returns the independently specified role text for a scalar or duplicate-order case.</summary>
    /// <param name="id">The accepted role case key selecting replacement, null ordering, omission, padded text, number, or Boolean behavior.</param>
    /// <returns>The expected role text, or null for all-null and omitted cases.</returns>
    public static string? Expected(string id) => id switch
    {
        "replace" or "null_before" => "last",
        "null_after" or "repeat_null" => "first",
        "empty_after" => "",
        "all_null" or "omitted" => null,
        "padded" => " ค่า ไทย ",
        "integer" => "-42",
        "decimal" => "12.50",
        "true" => "true",
        "false" => "false",
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    };

    /// <summary>Builds a role body with the selected scalar or duplicate-order scenario and a fixed peer property.</summary>
    /// <param name="field">The Name or Description property whose value sequence is under test.</param>
    /// <param name="id">The accepted scalar or duplicate-order case identifier selecting the emitted JSON members.</param>
    /// <returns>The role JSON object with the selected value sequence and the other text property fixed to fixed.</returns>
    public static string ValidBody(string field, string id)
    {
        var lower = field.ToLowerInvariant();
        var members = id switch
        {
            "replace" => $"\"{field}\":\"first\",\"{lower}\":\"last\"",
            "null_after" => $"\"{field}\":\"first\",\"{lower}\":null",
            "null_before" => $"\"{field}\":null,\"{lower}\":\"last\"",
            "empty_after" => $"\"{field}\":\"first\",\"{lower}\":\"\"",
            "repeat_null" => $"\"{field}\":\"first\",\"{lower}\":null,\"{field}\":null",
            "all_null" => $"\"{field}\":null,\"{lower}\":null",
            "omitted" => "",
            "padded" => $"\"{field}\":\" ค่า ไทย \"",
            "integer" => $"\"{field}\":-42",
            "decimal" => $"\"{field}\":12.50",
            "true" => $"\"{field}\":true",
            "false" => $"\"{field}\":false",
            _ => throw new ArgumentOutOfRangeException(nameof(id))
        };
        var other = field == "Name" ? "Description" : "Name";
        return "{" + (members.Length == 0 ? "" : members + ",") + $"\"{other}\":\"fixed\"" + "}";
    }

    /// <summary>Builds a role body with an invalid object or array occurrence at the requested duplicate position.</summary>
    /// <param name="field">The Name or Description property receiving the malformed value.</param>
    /// <param name="token">The object or array selector converted to an invalid JSON value for the text property.</param>
    /// <param name="position">The only, before, or after placement of the malformed value relative to a valid text occurrence.</param>
    /// <returns>The role JSON body containing the requested malformed occurrence order.</returns>
    public static string MalformedBody(string field, string token, string position)
    {
        var bad = token == "object" ? "{}" : token == "array" ? "[]"
            : throw new ArgumentOutOfRangeException(nameof(token));
        var member = $"\"{field}\":{bad}";
        return "{" + (position switch
        {
            "only" => member,
            "before" => member + $",\"{field.ToLowerInvariant()}\":\"last\"",
            "after" => $"\"{field}\":\"first\"," + member,
            _ => throw new ArgumentOutOfRangeException(nameof(position))
        }) + "}";
    }
}

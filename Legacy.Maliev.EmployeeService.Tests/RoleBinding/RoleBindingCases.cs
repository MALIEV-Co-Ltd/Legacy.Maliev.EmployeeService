namespace Legacy.Maliev.EmployeeService.Tests.RoleBinding;

// Literal expectations are independent of both readers. No original binary/provider claim.
public static class RoleBindingCases
{
    private static readonly string[] Fields = ["Name", "Description"];
    private static readonly string[] ValidIds =
        ["replace", "null_after", "null_before", "empty_after", "repeat_null", "all_null",
         "omitted", "padded", "integer", "decimal", "true", "false"];

    public static IEnumerable<object[]> Valid => Fields.SelectMany(member =>
        ValidIds.Select(id => new object[] { member, id }));

    public static IEnumerable<object[]> Malformed => Fields.SelectMany(member =>
        new[] { "object", "array" }.SelectMany(token =>
            new[] { "only", "before", "after" }.Select(position => new object[] { member, token, position })));

    public static IEnumerable<object[]> HttpValid => Fields.SelectMany(member =>
        new[] { "null_after", "true" }.SelectMany(id =>
            new[] { false, true }.Select(update => new object[] { member, id, update })));

    public static IEnumerable<object[]> HttpRejected => new[] { false, true }.SelectMany(update =>
        new[] { "null", "{\"Name\":{},\"Name\":\"last\"}", "{\"Description\":[],\"Description\":\"last\"}" }
            .Select(json => new object[] { update, json }));

    public static IEnumerable<object[]> Denied => new[] { false, true }.SelectMany(update =>
        new[] { "anonymous", "wrong-signature", "expired", "missing-permission" }
            .Select(authority => new object[] { update, authority }));

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

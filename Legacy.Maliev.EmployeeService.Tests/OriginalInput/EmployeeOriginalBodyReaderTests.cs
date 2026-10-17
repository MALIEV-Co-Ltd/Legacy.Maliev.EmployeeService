using System.Text;
using Legacy.Maliev.EmployeeService.Application.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Legacy.Maliev.EmployeeService.Tests.OriginalInput;

[Collection("Employee scaffold runtime")]
public sealed class EmployeeOriginalBodyReaderTests(EmployeeRouteAcceptanceFixture fixture)
    : IClassFixture<EmployeeRouteAcceptanceFixture>
{
    private static string Body(string kind) => kind switch
    {
        "employee" => """{"FirstName":"N","LastName":"L","Email":"e@x"}""",
        "address" => """{"Building":"B","CountryId":7}""",
        _ => """{"Bucket":"B","ObjectName":"O","EmployeeId":7}"""
    };

    private static string Projection(string kind) => kind switch
    {
        "employee" => """[null,"N","L",null,"e@x",null,null]""",
        "address" => """["B",null,null,null,null,null,7]""",
        _ => """["B","O",7]"""
    };

    public static IEnumerable<object[]> Cases()
    {
        foreach (var kind in new[] { "employee", "address", "signature" })
        {
            var body = Body(kind);
            var expected = Projection(kind);
            yield return Row(kind, "standard", body, "success", "success", expected);
            yield return Row(kind, "single quotes", body.Replace('"', '\''), "success", "failure", expected);
            yield return Row(kind, "unquoted names",
                System.Text.RegularExpressions.Regex.Replace(body, "\\\"([A-Za-z][A-Za-z0-9]*)\\\":", "$1:"),
                "success", "failure", expected);
            yield return Row(kind, "leading comments", "/* source */" + body, "success", "failure", expected);
            yield return Row(kind, "member comments", body.Insert(1, "/* source */"), "success", "failure", expected);
            yield return Row(kind, "trailing comma", body.Insert(body.Length - 1, ","), "success", "failure", expected);
            yield return Row(kind, "additional document", body + """{"Id":"private-input-marker"}""", "success", "failure", expected);
            yield return Row(kind, "trailing text", body + " private-input-marker", "success", "failure", expected);
            yield return Row(kind, "unknown skipped", body.Insert(1, "\"Unknown\":{\"$type\":null},"), "success", "success", expected);
            yield return Row(kind, "whole null", "null", "no-value", "no-value", "null");
            yield return Row(kind, "whitespace", "  ", "no-value", "failure", "null");
            yield return Row(kind, "no body", "", "no-value", "no-value", "null");
            yield return Row(kind, "empty string root", "\"\"", "no-value", "failure", "null");
            yield return Row(kind, "wrong string root", "\"wrong\"", "failure", "failure", "null");
            yield return Row(kind, "wrong array root", "[]", "failure", "failure", "null");
            yield return Row(kind, "unresolved reference", """{"$ref":"missing"}""", "no-value", "success", "null", null);
            yield return Row(kind, "invalid reference type", """{"$ref":42}""", "failure", "success", "null", null);
            yield return Row(kind, "reference additional fields", """{"$ref":"missing","Id":1}""", "failure", "success", "null", null);
            yield return Row(kind, "null reference marker", body.Insert(1, "\"$ref\":null,"), "success", "success", expected);
            yield return Row(kind, "type activation disabled", body.Insert(1,
                "\"$type\":\"System.IO.FileInfo, System.Private.CoreLib\","), "success", "success", expected);
            yield return Row(kind, "wrong values wrapper", """{"$values":[]}""", "failure", "success", "null", null);
            yield return Row(kind, "unexpected metadata exception", """{"$type":null}""",
                "throw:System.NullReferenceException", "success", "null", null);
        }

        yield return Row("employee", "source text and nullable scalar family",
            """{"FirstName":42,"LastName":false,"Email":"e@x","PhoneNumber":true,"RoleId":" +7 ","HomeAddressId":""}""",
            "success", "failure", """[7,"42","false","true","e@x",null,null]""");
        yield return Row("employee", "ordered consumed strings",
            """{"FirstName":"N","FirstName":null,"LastName":"L","LastName":null,"Email":"e@x","Email":null,"PhoneNumber":"P","PhoneNumber":null}""",
            "success", "success", """[null,"N","L","P","e@x",null,null]""",
            """[null,null,null,null,null,null,null]""");
        yield return Row("employee", "ordered nullable IDs",
            """{"FirstName":"N","LastName":"L","Email":"e@x","RoleId":7,"RoleId":null,"HomeAddressId":8,"HomeAddressId":null}""",
            "success", "success", """[7,"N","L",null,"e@x",null,8]""", Projection("employee"));
        yield return Row("employee", "ordered nullable date",
            """{"FirstName":"N","LastName":"L","Email":"e@x","DateOfBirth":"2000-01-02T00:00:00Z","DateOfBirth":null}""",
            "success", "success", """[null,"N","L",null,"e@x","2000-01-02T00:00:00Z",null]""", Projection("employee"));
        foreach (var field in new[] { "Id", "FullName", "CreatedDate", "ModifiedDate" })
            yield return Row("employee", "discarded invalid " + field,
                Body("employee").Insert(1, "\"" + field + "\":{\"private-input-marker\":1},"),
                "failure", "success", "null", Projection("employee"));
        foreach (var member in new[]
        {
            "\"HomeAddress\":42",
            "\"HomeAddress\":{\"CountryId\":true}",
            "\"HomeAddress\":{\"Employee\":[{\"RoleId\":{}}]}",
            "\"Role\":{\"Id\":[]}",
            "\"Role\":{\"Employee\":[{\"HomeAddress\":{\"ModifiedDate\":{}}}]}"
        })
            yield return Row("employee", "invalid ignored graph " + member,
                Body("employee").Insert(1, member + ","), "failure", "success", "null", Projection("employee"));
        yield return Row("employee", "earlier invalid graph",
            Body("employee").Insert(1, "\"HomeAddress\":{\"CountryId\":true},\"HomeAddress\":null,"),
            "failure", "success", "null", Projection("employee"));
        yield return Row("employee", "valid source graph cycle",
            Body("employee").Insert(1, "\"$id\":\"E\",\"HomeAddress\":{\"Employee\":[{\"$ref\":\"E\"}]},\"Role\":{\"Employee\":[{\"$ref\":\"E\"}]},"),
            "success", "success", Projection("employee"));
        foreach (var member in new[]
        {
            "\"Employee\":42",
            "\"Employee\":[{\"RoleId\":{}}]",
            "\"Employee\":[{\"FirstName\":{}}]",
            "\"Employee\":[{\"Role\":{\"Employee\":[{\"CreatedDate\":[]}]}}]"
        })
            yield return Row("address", "invalid ignored graph " + member,
                Body("address").Insert(1, member + ","), "failure", "success", "null", Projection("address"));
        yield return Row("address", "earlier invalid collection",
            Body("address").Insert(1, "\"Employee\":[{\"Id\":[]}],\"Employee\":[],"),
            "failure", "success", "null", Projection("address"));
        yield return Row("address", "valid source graph cycle",
            Body("address").Insert(1, "\"$id\":\"A\",\"Employee\":[{\"HomeAddress\":{\"$ref\":\"A\"}}],"),
            "success", "success", Projection("address"));
        yield return Row("signature", "Employee is truly unknown",
            Body("signature").Insert(1, "\"Employee\":[{\"RoleId\":{}}],"),
            "success", "success", Projection("signature"));

        // Source JsonTextReader normalizes lone escaped surrogates before
        // model/controller validation. Literal replacement characters stay valid.
        foreach (var kind in new[] { "employee", "address", "signature" })
        {
            var fields = kind switch
            {
                "employee" => new[] { "FirstName", "LastName" },
                "address" => new[] { "Building", "City" },
                _ => new[] { "Bucket", "ObjectName" }
            };
            foreach (var field in fields)
                foreach (var sample in new[]
                {
                    (Label: "lone high escape", Json: "\"\\ud800\"", Parsed: "\uFFFD", Native: "failure"),
                    (Label: "lone low escape", Json: "\"\\udc00\"", Parsed: "\uFFFD", Native: "failure"),
                    (Label: "valid escaped pair", Json: "\"\\ud83d\\ude00\"", Parsed: "\U0001F600", Native: "success"),
                    (Label: "literal replacement", Json: "\"\uFFFD\"", Parsed: "\uFFFD", Native: "success")
                })
                {
                    var body = Body(kind);
                    var json = body[..^1] + ",\"" + field + "\":" + sample.Json + "}";
                    var expected = kind switch
                    {
                        "employee" => System.Text.Json.JsonSerializer.Serialize(new object?[] { null,
                            field == "FirstName" ? sample.Parsed : "N",
                            field == "LastName" ? sample.Parsed : "L", null, "e@x", null, null }),
                        "address" => System.Text.Json.JsonSerializer.Serialize(new object?[] {
                            field == "Building" ? sample.Parsed : "B", null, null,
                            field == "City" ? sample.Parsed : null, null, null, 7 }),
                        _ => System.Text.Json.JsonSerializer.Serialize(new object?[] {
                            field == "Bucket" ? sample.Parsed : "B",
                            field == "ObjectName" ? sample.Parsed : "O", 7 })
                    };
                    yield return Row(kind, field + " " + sample.Label, json,
                        "success", sample.Native, expected, sample.Native == "success" ? expected : null);
                }
        }

        foreach (var kind in new[] { "address", "signature" })
        {
            var field = kind == "address" ? "CountryId" : "EmployeeId";
            var prefix = kind == "address" ? "\"Building\":\"B\"" : "\"Bucket\":\"B\",\"ObjectName\":\"O\"";
            var sourceZero = kind == "address" ? """["B",null,null,null,null,null,0]""" : """["B","O",0]""";
            var nativeOmitted = kind == "address" ? sourceZero : """["B","O",null]""";
            yield return Row(kind, "omitted consumed integer", "{" + prefix + "}", "success", "success", sourceZero, nativeOmitted);
            yield return Row(kind, "null consumed integer", "{" + prefix + ",\"" + field + "\":null}",
                "success", kind == "address" ? "failure" : "success", sourceZero, nativeOmitted);
            yield return Row(kind, "empty consumed integer", "{" + prefix + ",\"" + field + "\":\"\"}",
                "success", "failure", sourceZero);
            yield return Row(kind, "padded signed integer", "{" + prefix + ",\"" + field + "\":\" +7 \"}",
                "success", "failure", Projection(kind));
            yield return Row(kind, "ordered consumed integer null",
                "{" + prefix + ",\"" + field + "\":7,\"" + field + "\":null}",
                "success", kind == "address" ? "failure" : "success", Projection(kind), nativeOmitted);
            yield return Row(kind, "ordered consumed integer empty",
                "{" + prefix + ",\"" + field + "\":7,\"" + field + "\":\"\"}",
                "success", "failure", Projection(kind));
            foreach (var token in new[] { "\" \"", "true", "{}", "[]", "7.0", "2147483648" })
                yield return Row(kind, "invalid integer " + token,
                    "{" + prefix + ",\"" + field + "\":" + token + "}", "failure", "failure", "null");
        }
    }

    private static object[] Row(string kind, string label, string json, string source, string native,
        string projection, string? nativeProjection = null) =>
        [kind, label, json, source, native, projection, nativeProjection!];

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task SourceNativeAndCandidate_PinActualFrontdoorOutcomes(string kind, string label, string json,
        string sourceKind, string nativeKind, string projection, string? nativeProjection)
    {
        var source = await EmployeeOriginalInputOracle.ReadAsync(kind, json);
        Assert.Equal(sourceKind, source.Kind);
        if (source.Kind == "success") Assert.Equal(projection, EmployeeOriginalInputOracle.Projection(source.Model));
        var services = fixture.Factory.Services;
        var bytes = Encoding.UTF8.GetBytes(json);
        var native = await EmployeeOriginalBodyNative.ReadAsync(services, EmployeeOriginalBodyNative.Create(services), kind, bytes);
        Assert.Equal(nativeKind, native.Kind);
        if (native.Kind == "success" && nativeProjection is not null)
            Assert.Equal(nativeProjection, EmployeeOriginalInputOracle.Projection(native.Model));
        var candidate = await EmployeeOriginalBodyNative.ReadAsync(services,
            services.GetRequiredService<IOptions<MvcOptions>>().Value, kind, bytes);
        Assert.Equal(sourceKind, candidate.Kind);
        if (candidate.Kind == "success") Assert.Equal(projection, EmployeeOriginalInputOracle.Projection(candidate.Model));
        if (candidate.Kind == "failure")
        {
            Assert.True(candidate.ErrorCount > 0);
            Assert.DoesNotContain(candidate.Messages, message => message.Contains("private-input-marker", StringComparison.Ordinal));
        }
        _ = label;
    }

    [Theory]
    [InlineData("employee", 31, "success")]
    [InlineData("employee", 32, "success")]
    [InlineData("employee", 33, "failure")]
    [InlineData("address", 31, "success")]
    [InlineData("address", 32, "success")]
    [InlineData("address", 33, "failure")]
    [InlineData("signature", 31, "success")]
    [InlineData("signature", 32, "success")]
    [InlineData("signature", 33, "failure")]
    public async Task BothMvcDepthDefaults_Are32(string kind, int depth, string expected)
    {
        var json = EmployeeOriginalInputOracle.NestedUnknown(depth);
        var source = await EmployeeOriginalInputOracle.ReadAsync(kind, json);
        Assert.Equal(expected, source.Kind);
        var services = fixture.Factory.Services;
        var bytes = Encoding.UTF8.GetBytes(json);
        var native = await EmployeeOriginalBodyNative.ReadAsync(services, EmployeeOriginalBodyNative.Create(services), kind, bytes);
        Assert.Equal(expected, native.Kind);
        var candidate = await EmployeeOriginalBodyNative.ReadAsync(services,
            services.GetRequiredService<IOptions<MvcOptions>>().Value, kind, bytes);
        Assert.Equal(expected, candidate.Kind);
    }

    public static IEnumerable<object[]> Encodings()
    {
        foreach (var kind in new[] { "employee", "address", "signature" })
        {
            yield return [kind, "application/json", false, "success"];
            yield return [kind, "text/json", false, "success"];
            yield return [kind, "application/vnd.fixture+json", false, "success"];
            yield return [kind, "application/json; charset=utf-16", true, "success"];
            yield return [kind, "application/json; charset=x-private-charset-marker", false, "failure"];
            yield return [kind, "application/json; charset=utf-16BE", false, "failure"];
            yield return [kind, "application/xml", false, "unsupported"];
        }
    }

    [Theory]
    [MemberData(nameof(Encodings))]
    public async Task StockMediaCharsetsAndSafe415_AreRetained(string kind, string contentType, bool unicode, string expected)
    {
        var bytes = (unicode ? Encoding.Unicode : Encoding.UTF8).GetBytes(Body(kind));
        var source = await EmployeeOriginalInputOracle.ReadBytesAsync(EmployeeOriginalInputOracle.Formatter(),
            EmployeeOriginalInputOracle.SourceType(kind), new EmptyModelMetadataProvider(), bytes, contentType);
        Assert.Equal(expected, source.Kind);
        var services = fixture.Factory.Services;
        var candidate = await EmployeeOriginalBodyNative.ReadAsync(services,
            services.GetRequiredService<IOptions<MvcOptions>>().Value, kind, bytes, contentType);
        Assert.Equal(expected, candidate.Kind);
        if (expected == "success") Assert.Equal(Projection(kind), EmployeeOriginalInputOracle.Projection(candidate.Model));
        if (expected == "failure")
        {
            Assert.Contains(typeof(UnsupportedContentTypeException).FullName!, candidate.ExceptionTypes);
            Assert.DoesNotContain(candidate.Messages, message => message.Contains("x-private-charset-marker", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task ReferencesArePerRead_AndOnlyRecognizedGraphKeysCanEscape()
    {
        var services = fixture.Factory.Services;
        var options = services.GetRequiredService<IOptions<MvcOptions>>().Value;
        var first = await EmployeeOriginalBodyNative.ReadAsync(services, options, "address",
            Encoding.UTF8.GetBytes("""{"$id":"previous","Building":"B","CountryId":7}"""));
        Assert.Equal("success", first.Kind);
        var second = await EmployeeOriginalBodyNative.ReadAsync(services, options, "address",
            Encoding.UTF8.GetBytes("""{"$ref":"previous"}"""));
        Assert.Equal("no-value", second.Kind);
        const string graph = """{"Employee":[{"HomeAddress":{"CountryId":"private-input-marker"}}]}""";
        var source = await EmployeeOriginalInputOracle.ReadAsync("address", graph, prefix: "item");
        var candidate = await EmployeeOriginalBodyNative.ReadAsync(services, options, "address",
            Encoding.UTF8.GetBytes(graph), prefix: "item");
        Assert.Equal("failure", candidate.Kind);
        Assert.Equal(source.Keys, candidate.Keys);
        Assert.Contains("item.Employee[0].HomeAddress.CountryId", candidate.Keys);
        Assert.DoesNotContain(candidate.Messages, message => message.Contains("private-input-marker", StringComparison.Ordinal));
        var unknown = await EmployeeOriginalBodyNative.ReadAsync(services, options, "employee",
            Encoding.UTF8.GetBytes("""{"private-key-marker":[}"""));
        Assert.Equal("failure", unknown.Kind);
        Assert.All(unknown.Keys, key => Assert.Equal("$", key));
        Assert.DoesNotContain(unknown.Messages, message => message.Contains("private-key-marker", StringComparison.Ordinal));
        var bounded = await EmployeeOriginalBodyNative.ReadAsync(services, options, "employee",
            Encoding.UTF8.GetBytes("""{"Id":"bad","CreatedDate":"bad","ModifiedDate":"bad"}"""), maxErrors: 2);
        Assert.Equal("failure", bounded.Kind);
        Assert.InRange(bounded.ErrorCount, 1, 2);
    }

    [Fact]
    public void OriginalMvcSettings_AreNotJsonConvertConvenienceDefaults()
    {
        var options = EmployeeOriginalInputOracle.OriginalOptions();
        Assert.Equal(32, options.SerializerSettings.MaxDepth);
        Assert.False(options.SerializerSettings.CheckAdditionalContent);
        Assert.Equal(TypeNameHandling.None, options.SerializerSettings.TypeNameHandling);
        Assert.Equal(NullValueHandling.Ignore, options.SerializerSettings.NullValueHandling);
        Assert.Equal(ReferenceLoopHandling.Ignore, options.SerializerSettings.ReferenceLoopHandling);
        Assert.False(options.ReadJsonWithRequestCulture);
        Assert.Equal(30 * 1024, options.InputFormatterMemoryBufferThreshold);
    }
}

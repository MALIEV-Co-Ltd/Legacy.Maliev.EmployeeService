using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.EmployeeService.Application.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.EmployeeService.Tests.OriginalInput;

/// <summary>Verifies original-body formatter selection, native serializer isolation, served schemas, and private diagnostics.</summary>
/// <param name="fixture">The shared Employee route fixture supplying the production application factory, PostgreSQL contexts, Redis, and controlled IAM request authority.</param>
[Collection("Employee scaffold runtime")]
public sealed class EmployeeOriginalBodyBoundaryTests(EmployeeRouteAcceptanceFixture fixture)
    : IClassFixture<EmployeeRouteAcceptanceFixture>
{
    private const string FormatterName = EmployeeOriginalBodyNative.FormatterName;
    private const string FormatterCategory = "Microsoft.AspNetCore.Mvc.Formatters.NewtonsoftJsonInputFormatter";

    /// <summary>Checks formatter selection for exactly employee, address, and signature bodies without reordering native readers or writers.</summary>
    [Fact]
    public void Selection_IsExactlyThreeDtoBodiesAndPreservesNativeFormatterOrder()
    {
        var services = fixture.Factory.Services;
        var candidate = services.GetRequiredService<IOptions<MvcOptions>>().Value;
        var native = EmployeeOriginalBodyNative.Create(services);
        Assert.NotSame(candidate, native);
        var owned = Assert.Single(candidate.InputFormatters, value => value.GetType().FullName == FormatterName);
        Assert.Same(owned, candidate.InputFormatters[0]);
        Assert.Equal(native.InputFormatters.Select(value => value.GetType()).ToArray(),
            candidate.InputFormatters.Where(value => !ReferenceEquals(value, owned)).Select(value => value.GetType()).ToArray());
        Assert.Equal(native.OutputFormatters.Select(value => value.GetType()).ToArray(),
            candidate.OutputFormatters.Select(value => value.GetType()).ToArray());
        Assert.Single(candidate.InputFormatters, value => value is SystemTextJsonInputFormatter);
        Assert.DoesNotContain(candidate.OutputFormatters, value => value.GetType().FullName?.Contains("Newtonsoft", StringComparison.Ordinal) == true);
        var metadata = services.GetRequiredService<IModelMetadataProvider>();
        foreach (var type in new[] { typeof(UpsertEmployeeRequest), typeof(UpsertAddressRequest), typeof(UpsertSignatureImageFileRequest) })
        {
            Assert.True(owned.CanRead(Context(type, metadata)));
            Assert.True(owned.CanRead(Context(type, metadata, "application/vnd.employee+json")));
        }
        foreach (var type in new[] { typeof(UpsertRoleRequest), typeof(UpdateEmployeeSelfProfileRequest),
            typeof(EmployeeHomeAddressEditRequest), typeof(EmployeeResponse) })
            Assert.False(owned.CanRead(Context(type, metadata)));
    }

    /// <summary>Checks that native option construction and owned body reads preserve serializer callbacks, converters, metadata, and writers.</summary>
    /// <returns>A task that completes after verifying that native option construction and owned body reads preserve serializer callbacks, converters, metadata, and writers.</returns>
    [Fact]
    public async Task FreshNativeOptionsAndOwnedRead_LeaveJsonCallbacksReadersAndWritersUnchanged()
    {
        var services = fixture.Factory.Services;
        var json = services.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>().Value.JsonSerializerOptions;
        var minimal = services.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
        var setups = services.GetServices<IConfigureOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>().ToArray();
        JsonSerializer.Serialize<object?>(null, json);
        var converters = json.Converters.ToArray();
        var resolver = json.TypeInfoResolver;
        var chain = json.TypeInfoResolverChain.ToArray();
        var types = new[] { typeof(UpsertEmployeeRequest), typeof(UpsertAddressRequest), typeof(UpsertSignatureImageFileRequest),
            typeof(UpsertRoleRequest), typeof(UpdateEmployeeSelfProfileRequest), typeof(EmployeeHomeAddressEditRequest), typeof(EmployeeResponse) };
        var metadata = types.Select(json.GetTypeInfo).ToArray();
        var creators = metadata.Select(value => value.CreateObject).ToArray();
        var readers = metadata.Select(value => value.Properties.Select(property => property.CustomConverter).ToArray()).ToArray();
        var samples = new object[]
        {
            new UpsertEmployeeRequest(null, " literal ไทย ", " family ", null, "literal@example.invalid", null, null),
            new UpsertAddressRequest(null, " literal ไทย ", null, null, null, null, 764),
            new UpsertSignatureImageFileRequest(" literal ", " literal ไทย.png ", null)
        };
        var before = samples.Select(value => JsonSerializer.Serialize(value, value.GetType(), json)).ToArray();
        var native = EmployeeOriginalBodyNative.Create(services);
        var candidate = services.GetRequiredService<IOptions<MvcOptions>>().Value;
        var actual = await EmployeeOriginalBodyNative.ReadAsync(services, candidate, "address",
            Encoding.UTF8.GetBytes("{\"Building\":123,\"CountryId\":764}"));
        Assert.Equal("success", actual.Kind);
        Assert.Equal("123", Assert.IsType<UpsertAddressRequest>(actual.Model).Building);
        Assert.Single(native.InputFormatters, value => value is SystemTextJsonInputFormatter);
        Assert.Same(json, services.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>().Value.JsonSerializerOptions);
        Assert.Same(minimal, services.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions);
        Assert.Same(resolver, json.TypeInfoResolver);
        Assert.Equal(chain.Length, json.TypeInfoResolverChain.Count);
        for (var index = 0; index < chain.Length; index++) Assert.Same(chain[index], json.TypeInfoResolverChain[index]);
        Assert.Equal(converters.Length, json.Converters.Count);
        for (var index = 0; index < converters.Length; index++) Assert.Same(converters[index], json.Converters[index]);
        var retainedSetups = services.GetServices<IConfigureOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>().ToArray();
        Assert.Equal(setups.Length, retainedSetups.Length);
        for (var index = 0; index < setups.Length; index++) Assert.Same(setups[index], retainedSetups[index]);
        for (var index = 0; index < types.Length; index++)
        {
            var current = json.GetTypeInfo(types[index]);
            Assert.Same(metadata[index], current);
            Assert.Same(creators[index], current.CreateObject);
            Assert.Equal(readers[index].Length, current.Properties.Count);
            for (var property = 0; property < readers[index].Length; property++)
                Assert.Same(readers[index][property], current.Properties[property].CustomConverter);
        }
        Assert.Equal(before, samples.Select(value => JsonSerializer.Serialize(value, value.GetType(), json)).ToArray());
        Assert.Equal("123", JsonSerializer.Deserialize<UpsertAddressRequest>("{\"Building\":123}", json)!.Building);
        Assert.Equal("true", JsonSerializer.Deserialize<UpsertSignatureImageFileRequest>("{\"Bucket\":true}", json)!.Bucket);
        Assert.Equal("123", JsonSerializer.Deserialize<UpsertRoleRequest>("{\"Name\":123}", json)!.Name);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UpsertRoleRequest>("{\"Name\":123}", minimal));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UpdateEmployeeSelfProfileRequest>("{\"FirstName\":123}", json));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<EmployeeHomeAddressEditRequest>("{\"Building\":123}", json));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<EmployeeResponse>("{\"FirstName\":123}", json));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UpsertAddressRequest>("{\"Building\":123}", minimal));
    }

    /// <summary>Checks native DTO schema and request-media preservation in the served OpenAPI document.</summary>
    /// <param name="kind">The employee, address, or signature body kind selecting the request DTO.</param>
    /// <param name="schemaName">The corresponding UpsertEmployeeRequest, UpsertAddressRequest, or UpsertSignatureImageFileRequest schema name.</param>
    /// <returns>A task that completes after verifying native DTO schema and request-media preservation in the served OpenAPI document.</returns>
    [Theory]
    [InlineData("employee", "UpsertEmployeeRequest")]
    [InlineData("address", "UpsertAddressRequest")]
    [InlineData("signature", "UpsertSignatureImageFileRequest")]
    public async Task ServedOpenApi_PreservesNativeDtoSchemaAndRequestMedia(string kind, string schemaName)
    {
        await using var candidate = fixture.DocumentationFactory();
        await using var native = fixture.DocumentationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(EmployeeOriginalBodyNative.RemoveOwned));
        using var candidateClient = candidate.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var nativeClient = native.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var actual = await DocumentAsync(candidateClient);
        var expected = await DocumentAsync(nativeClient);
        var actualSchema = actual.GetProperty("components").GetProperty("schemas").GetProperty(schemaName);
        var expectedSchema = expected.GetProperty("components").GetProperty("schemas").GetProperty(schemaName);
        Assert.Equal(Canonical(expectedSchema), Canonical(actualSchema));
        Assert.Equal(EmployeeOriginalInputOracle.DtoType(kind).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(value => value.Name).Order(StringComparer.Ordinal).ToArray(),
            actualSchema.GetProperty("properties").EnumerateObject().Select(value => value.Name).Order(StringComparer.Ordinal).ToArray());
        var nativeMedia = RequestMedia(expected, schemaName);
        Assert.NotEmpty(nativeMedia);
        Assert.Equal(nativeMedia, RequestMedia(actual, schemaName));
    }

    /// <summary>Checks nonempty debug formatter diagnostics without exposing input paths, values, charset names, or exception objects.</summary>
    /// <returns>A task that completes after verifying nonempty debug formatter diagnostics without exposing input paths, values, charset names, or exception objects.</returns>
    [Fact]
    public async Task ActualFormatterDiagnostics_AreNonemptyAndExcludePrivatePathsValuesCharsetsAndExceptions()
    {
        var capture = new FormatterCapture();
        using var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug).AddProvider(capture));
        var type = typeof(Program).Assembly.GetType(FormatterName, throwOnError: true)!;
        var constructor = type.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
            [typeof(MvcOptions), typeof(ILogger)], null);
        Assert.NotNull(constructor);
        var formatter = Assert.IsAssignableFrom<IInputFormatter>(constructor.Invoke(
            [new MvcOptions(), loggerFactory.CreateLogger(FormatterCategory)]));
        var metadata = fixture.Factory.Services.GetRequiredService<IModelMetadataProvider>();
        string[] markers = ["private-path-71", "private-value-42", "private-charset-89", "private-prefix-54"];
        var cases = new[]
        {
            ("employee", "{\"private-path-71\":{\"deep\":[0,", "application/json"),
            ("address", "{\"CountryId\":\"private-value-42\"}", "application/json"),
            ("signature", "{\"Bucket\":\"private-value-42\",\"EmployeeId\":{}}", "application/json"),
            ("employee", "{}", "application/json; charset=private-charset-89")
        };
        foreach (var (kind, body, contentType) in cases)
        {
            var result = await EmployeeOriginalInputOracle.ReadBytesAsync(formatter, EmployeeOriginalInputOracle.DtoType(kind),
                metadata, Encoding.UTF8.GetBytes(body), contentType, "private-prefix-54");
            Assert.Equal("failure", result.Kind);
            Assert.True(result.ErrorCount > 0);
            AssertPrivate(string.Join("\n", result.Keys.Concat(result.Messages)), markers);
        }
        Assert.NotEmpty(capture.Records);
        Assert.Contains(capture.Records, value => value.Level == LogLevel.Debug);
        Assert.All(capture.Records, value =>
        {
            Assert.Equal(FormatterCategory, value.Category);
            Assert.Null(value.Exception);
            AssertPrivate(value.Rendered + "\n" + string.Join("\n", value.State), markers);
        });
        Assert.All(capture.Scopes, value => AssertPrivate(value, markers));
        // Direct formatter logging only: retained BodyModelBinder Debug logs are
        // outside this claim. Normal production logging stays at Warning.
    }

    private static InputFormatterContext Context(Type type, IModelMetadataProvider metadata,
        string contentType = "application/json")
    {
        var http = new DefaultHttpContext();
        http.Request.ContentType = contentType;
        return new(http, "", new ModelStateDictionary(), metadata.GetMetadataForType(type),
            static (stream, encoding) => new HttpRequestStreamReader(stream, encoding), false);
    }

    private static async Task<JsonElement> DocumentAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/employee/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static string[] RequestMedia(JsonElement document, string name) =>
        document.GetProperty("paths").EnumerateObject().SelectMany(path => path.Value.EnumerateObject()
            .Where(operation => operation.Value.ValueKind == JsonValueKind.Object
                && operation.Value.TryGetProperty("requestBody", out _))
            .SelectMany(operation => operation.Value.GetProperty("requestBody").GetProperty("content").EnumerateObject()
                .Where(media => References(media.Value.GetProperty("schema"), "#/components/schemas/" + name))
                .Select(media => path.Name + "|" + operation.Name + "|" + media.Name + "|" + Canonical(media.Value.GetProperty("schema")))))
            .Order(StringComparer.Ordinal).ToArray();

    private static bool References(JsonElement value, string reference) => value.ValueKind switch
    {
        JsonValueKind.Object => value.EnumerateObject().Any(property =>
            property.Name == "$ref" && property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() == reference
            || References(property.Value, reference)),
        JsonValueKind.Array => value.EnumerateArray().Any(item => References(item, reference)),
        _ => false
    };

    private static string Canonical(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(",", value.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal)
            .Select(item => JsonSerializer.Serialize(item.Name) + ":" + Canonical(item.Value))) + "}",
        JsonValueKind.Array => "[" + string.Join(",", value.EnumerateArray().Select(Canonical)) + "]",
        JsonValueKind.String => JsonSerializer.Serialize(value.GetString()),
        _ => value.GetRawText()
    };

    private static void AssertPrivate(string value, string[] markers)
    {
        foreach (var marker in markers) Assert.DoesNotContain(marker, value, StringComparison.Ordinal);
    }

    private sealed record CapturedLog(string Category, LogLevel Level, string Rendered, string[] State, Exception? Exception);

    private sealed class FormatterCapture : ILoggerProvider
    {
        internal ConcurrentQueue<CapturedLog> Records { get; } = new();
        internal ConcurrentQueue<string> Scopes { get; } = new();
        public ILogger CreateLogger(string categoryName) => categoryName == FormatterCategory
            ? new CaptureLogger(this, categoryName) : NullLogger.Instance;
        public void Dispose() { }

        private sealed class CaptureLogger(FormatterCapture owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                owner.Scopes.Enqueue(Convert.ToString(state, CultureInfo.InvariantCulture) ?? "");
                return null;
            }
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var values = state is IEnumerable<KeyValuePair<string, object?>> structured
                    ? structured.Select(value => value.Key + "=" + Convert.ToString(value.Value, CultureInfo.InvariantCulture)).ToArray()
                    : [Convert.ToString(state, CultureInfo.InvariantCulture) ?? ""];
                owner.Records.Enqueue(new(category, logLevel, formatter(state, exception), values, exception));
            }
        }
    }
}

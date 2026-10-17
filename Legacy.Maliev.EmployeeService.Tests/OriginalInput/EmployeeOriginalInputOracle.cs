using System.Buffers;
using System.Text;
using Legacy.Maliev.EmployeeService.Application.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.ObjectPool;
using Newtonsoft.Json;
using Stj = System.Text.Json.JsonSerializer;

namespace Legacy.Maliev.EmployeeService.Tests.OriginalInput;

// Actual stock MVC reader, independent source shapes and original settings.
// JsonConvert's additional-content convenience default is not this frontdoor.
internal static class EmployeeOriginalInputOracle
{
    internal static MvcNewtonsoftJsonOptions OriginalOptions()
    {
        var options = new MvcNewtonsoftJsonOptions();
        options.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
        options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
        return options;
    }

    internal static NewtonsoftJsonInputFormatter Formatter()
    {
        var options = OriginalOptions();
        return new NewtonsoftJsonInputFormatter(NullLogger<NewtonsoftJsonInputFormatter>.Instance,
            options.SerializerSettings, ArrayPool<char>.Shared, new DefaultObjectPoolProvider(), new MvcOptions(), options);
    }

    internal static Type SourceType(string kind) => kind switch
    {
        "employee" => typeof(SourceEmployee),
        "address" => typeof(SourceAddress),
        "signature" => typeof(SourceSignature),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    internal static Type DtoType(string kind) => kind switch
    {
        "employee" => typeof(UpsertEmployeeRequest),
        "address" => typeof(UpsertAddressRequest),
        "signature" => typeof(UpsertSignatureImageFileRequest),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    internal static Task<EmployeeBodyRead> ReadAsync(string kind, string json, string contentType = "application/json", string prefix = "") =>
        ReadBytesAsync(Formatter(), SourceType(kind), new EmptyModelMetadataProvider(),
            Encoding.UTF8.GetBytes(json), contentType, prefix);

    internal static async Task<EmployeeBodyRead> ReadBytesAsync(IInputFormatter formatter, Type modelType,
        IModelMetadataProvider metadataProvider, byte[] bytes, string contentType, string prefix = "", int maxErrors = 200)
    {
        var http = new DefaultHttpContext();
        using var input = new MemoryStream(bytes, writable: false);
        http.Request.Body = input;
        http.Request.ContentType = contentType;
        http.Request.ContentLength = bytes.LongLength;
        var state = new ModelStateDictionary { MaxAllowedErrors = maxErrors };
        var context = new InputFormatterContext(http, prefix, state, metadataProvider.GetMetadataForType(modelType),
            static (stream, encoding) => new HttpRequestStreamReader(stream, encoding), false);
        try
        {
            if (!formatter.CanRead(context)) return Result("unsupported", null, state);
            var result = await formatter.ReadAsync(context);
            return Result(result.HasError ? "failure" : result.IsModelSet ? "success" : "no-value", result.Model, state);
        }
        catch (Exception error)
        {
            // Observation only: callers pin an explicit expected throw type.
            return Result("throw:" + error.GetType().FullName, null, state);
        }
    }

    private static EmployeeBodyRead Result(string kind, object? model, ModelStateDictionary state) => new(
        kind, model, state.Keys.Order(StringComparer.Ordinal).ToArray(), state.ErrorCount,
        state.Values.SelectMany(value => value.Errors).Select(error => error.Exception?.GetType().FullName ?? "").ToArray(),
        state.Values.SelectMany(value => value.Errors).Select(error => error.ErrorMessage.Length == 0
            ? error.Exception?.Message ?? "" : error.ErrorMessage).ToArray());

    internal static string Projection(object? model) => model switch
    {
        null => "null",
        SourceEmployee value => Stj.Serialize(new object?[] { value.RoleId, value.FirstName, value.LastName,
            value.PhoneNumber, value.Email, value.DateOfBirth, value.HomeAddressId }),
        UpsertEmployeeRequest value => Stj.Serialize(new object?[] { value.RoleId, value.FirstName, value.LastName,
            value.PhoneNumber, value.Email, value.DateOfBirth, value.HomeAddressId }),
        SourceAddress value => Stj.Serialize(new object?[] { value.Building, value.AddressLine1,
            value.AddressLine2, value.City, value.State, value.PostalCode, value.CountryId }),
        UpsertAddressRequest value => Stj.Serialize(new object?[] { value.Building, value.AddressLine1,
            value.AddressLine2, value.City, value.State, value.PostalCode, value.CountryId }),
        SourceSignature value => Stj.Serialize(new object?[] { value.Bucket, value.ObjectName, value.EmployeeId }),
        UpsertSignatureImageFileRequest value => Stj.Serialize(new object?[] { value.Bucket, value.ObjectName, value.EmployeeId }),
        _ => throw new InvalidOperationException("Unexpected fixture model type.")
    };

    internal static string NestedUnknown(int depth) =>
        "{\"extra\":" + new string('[', depth - 1) + "0" + new string(']', depth - 1) + "}";
}

internal sealed record EmployeeBodyRead(string Kind, object? Model, string[] Keys, int ErrorCount,
    string[] ExceptionTypes, string[] Messages);

#nullable disable
// Original source project/model reference metadata was oblivious, with no MVC
// validation attributes. These do not import EF mappings or target DTO metadata.
internal sealed class SourceEmployee
{
    public int Id { get; set; }
    public int? RoleId { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string FullName { get; set; }
    public string PhoneNumber { get; set; }
    public string Email { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public int? HomeAddressId { get; set; }
    public DateTime? CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public SourceAddress HomeAddress { get; set; }
    public SourceRole Role { get; set; }
}

internal sealed class SourceAddress
{
    public SourceAddress() => Employee = new HashSet<SourceEmployee>();
    public int Id { get; set; }
    public string Building { get; set; }
    public string AddressLine1 { get; set; }
    public string AddressLine2 { get; set; }
    public string City { get; set; }
    public string State { get; set; }
    public string PostalCode { get; set; }
    public int CountryId { get; set; }
    public DateTime? CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public ICollection<SourceEmployee> Employee { get; set; }
}

internal sealed class SourceRole
{
    public SourceRole() => Employee = new HashSet<SourceEmployee>();
    public int Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public DateTime? CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public ICollection<SourceEmployee> Employee { get; set; }
}

internal sealed class SourceSignature
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string Bucket { get; set; }
    public string ObjectName { get; set; }
    public DateTime? CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
}
#nullable restore

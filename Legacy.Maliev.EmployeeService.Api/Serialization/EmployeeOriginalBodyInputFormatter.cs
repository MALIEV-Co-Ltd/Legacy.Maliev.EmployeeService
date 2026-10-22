using System.Buffers;
using System.Globalization;
using Legacy.Maliev.EmployeeService.Application.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Legacy.Maliev.EmployeeService.Api.Serialization;

internal sealed class EmployeeOriginalBodyMvcOptionsSetup(ILoggerFactory loggerFactory) : IConfigureOptions<MvcOptions>
{
    public void Configure(MvcOptions options)
    {
        var original = options.ModelBinderProviders.Select((provider, index) => (provider, index))
            .Single(item => item.provider.GetType() == typeof(BodyModelBinderProvider));
        options.InputFormatters.Insert(0, new EmployeeOriginalBodyInputFormatter(options,
            loggerFactory.CreateLogger<NewtonsoftJsonInputFormatter>()));
        options.ModelBinderProviders[original.index] = new EmployeeOriginalBodyModelBinderProvider(original.provider);
    }
}

// Input-only composition. Do not subclass the stock formatter: its exception
// policy would change. Public DTO/schema/output and the existing STJ readers stay.
internal sealed class EmployeeOriginalBodyInputFormatter : IInputFormatter,
    IApiRequestFormatMetadataProvider, IInputFormatterExceptionPolicy
{
    private readonly NewtonsoftJsonInputFormatter inner;
    private readonly EmptyModelMetadataProvider metadata = new();

    internal EmployeeOriginalBodyInputFormatter(MvcOptions options, ILogger logger)
    {
        var original = new MvcNewtonsoftJsonOptions();
        original.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
        original.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
        // Source MVC defaults: MaxDepth32, TypeNameHandling.None,
        // CheckAdditionalContent=false and a per-deserialization reference map.
        inner = new NewtonsoftJsonInputFormatter(new PrivateFormatterLogger(logger),
            original.SerializerSettings, ArrayPool<char>.Shared, new DefaultObjectPoolProvider(), options, original);
    }

    internal static bool IsSelected(Type type) => SourceType(type) is not null;

    public InputFormatterExceptionPolicy ExceptionPolicy => InputFormatterExceptionPolicy.MalformedInputExceptions;

    public bool CanRead(InputFormatterContext context) => SourceType(context.ModelType) is { } type
        && inner.CanRead(Context(context, type, new ModelStateDictionary()));

    public IReadOnlyList<string>? GetSupportedContentTypes(string? contentType, Type objectType) =>
        SourceType(objectType) is { } type ? inner.GetSupportedContentTypes(contentType, type) : null;

    public async Task<InputFormatterResult> ReadAsync(InputFormatterContext context)
    {
        context.HttpContext.Items.Remove(EmployeeOriginalBodyProjection.Key);
        var type = SourceType(context.ModelType)
            ?? throw new InvalidOperationException("Unsupported Employee request type.");
        var state = new ModelStateDictionary { MaxAllowedErrors = context.ModelState.MaxAllowedErrors };
        InputFormatterResult result;
        try
        {
            // Preserve stock charset selection, buffering/drain policy, exception
            // handling and resource ownership, not a JsonConvert approximation.
            result = await inner.ReadAsync(Context(context, type, state));
        }
        finally
        {
            foreach (var entry in state)
            {
                var key = SafePath(entry.Key, context.ModelName, Shape(type));
                foreach (var error in entry.Value.Errors)
                {
                    if (error.Exception is UnsupportedContentTypeException)
                    {
                        context.ModelState.TryAddModelError(key,
                            new UnsupportedContentTypeException("Unsupported request content type."), context.Metadata);
                    }
                    else
                    {
                        context.ModelState.TryAddModelError(key, "Invalid request body.");
                    }
                }
            }
        }

        if (result.HasError || !result.IsModelSet) return result;
        object? model = result.Model switch
        {
            OriginalEmployeeInput employee => new UpsertEmployeeRequest(employee.RoleId,
                employee.FirstName, employee.LastName, employee.PhoneNumber, employee.Email,
                employee.DateOfBirth, employee.HomeAddressId),
            OriginalAddressInput address => new UpsertAddressRequest(address.Building, address.AddressLine1,
                address.AddressLine2, address.City, address.State, address.PostalCode, address.CountryId),
            // Original EmployeeId is nonnullable and ALWAYS written by source PUT:
            // omitted/null/exact empty means0; duplicate null/empty retains prior value.
            OriginalSignatureInput signature => new UpsertSignatureImageFileRequest(signature.Bucket,
                signature.ObjectName, signature.EmployeeId),
            null => null,
            _ => throw new InvalidOperationException("Unexpected Employee input model.")
        };
        var success = await InputFormatterResult.SuccessAsync(model);
        if (model is not null) context.HttpContext.Items[EmployeeOriginalBodyProjection.Key] = model;
        // Only existing DTO scalar slots cross this boundary. Navigation, identity,
        // FullName and audit inputs are parsed but never attached to EF tracking.
        return success;
    }

    private InputFormatterContext Context(InputFormatterContext outer, Type type, ModelStateDictionary state) =>
        new(outer.HttpContext, outer.ModelName, state, metadata.GetMetadataForType(type),
            outer.ReaderFactory, outer.TreatEmptyInputAsDefaultValue);

    private static Type? SourceType(Type type) => type == typeof(UpsertEmployeeRequest)
        ? typeof(OriginalEmployeeInput) : type == typeof(UpsertAddressRequest) ? typeof(OriginalAddressInput)
        : type == typeof(UpsertSignatureImageFileRequest) ? typeof(OriginalSignatureInput) : null;

    private static InputShape Shape(Type type) => type == typeof(OriginalEmployeeInput)
        ? InputShape.Employee : type == typeof(OriginalAddressInput) ? InputShape.Address : InputShape.Signature;

    private static string SafePath(string key, string prefix, InputShape shape)
    {
        if (key.Length > 512 || prefix is not ("" or "item" or "request")) return "$";
        var path = key;
        if (prefix.Length != 0)
        {
            if (key == prefix) return prefix;
            if (!key.StartsWith(prefix + ".", StringComparison.Ordinal)) return "$";
            path = key[(prefix.Length + 1)..];
        }
        if (path.Length == 0) return key;
        var cursor = 0;
        while (cursor < path.Length)
        {
            var start = cursor;
            while (cursor < path.Length && path[cursor] is not ('.' or '[')) cursor++;
            var name = path[start..cursor];
            var last = cursor == path.Length;
            if (name is "$id" or "$ref" or "$type" or "$values") return last ? key : "$";
            if (Scalar(shape, name)) return last ? key : "$";
            var collection = shape is InputShape.Address or InputShape.Role
                && name.Equals("Employee", StringComparison.OrdinalIgnoreCase);
            if (shape == InputShape.Employee && name.Equals("HomeAddress", StringComparison.OrdinalIgnoreCase))
                shape = InputShape.Address;
            else if (shape == InputShape.Employee && name.Equals("Role", StringComparison.OrdinalIgnoreCase))
                shape = InputShape.Role;
            else if (collection)
                shape = InputShape.Employee;
            else
                return "$";
            if (last) return key;
            if (collection)
            {
                if (path[cursor++] != '[') return "$";
                var indexStart = cursor;
                while (cursor < path.Length && char.IsAsciiDigit(path[cursor])) cursor++;
                if (cursor == indexStart || cursor >= path.Length || path[cursor] != ']'
                    || !int.TryParse(path.AsSpan(indexStart, cursor - indexStart), NumberStyles.None,
                        CultureInfo.InvariantCulture, out _)) return "$";
                cursor++;
                if (cursor == path.Length) return key;
            }
            if (path[cursor++] != '.' || cursor == path.Length) return "$";
        }
        return "$";
    }

    private static bool Scalar(InputShape shape, string name)
    {
        if (name.Equals("Id", StringComparison.OrdinalIgnoreCase)
            || name.Equals("CreatedDate", StringComparison.OrdinalIgnoreCase)
            || name.Equals("ModifiedDate", StringComparison.OrdinalIgnoreCase)) return true;
        string[] fields = shape switch
        {
            InputShape.Employee => ["RoleId", "FirstName", "LastName", "FullName", "PhoneNumber",
                "Email", "DateOfBirth", "HomeAddressId"],
            InputShape.Address => ["Building", "AddressLine1", "AddressLine2", "City", "State", "PostalCode", "CountryId"],
            InputShape.Role => ["Name", "Description"],
            _ => ["EmployeeId", "Bucket", "ObjectName"]
        };
        return fields.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    private enum InputShape { Employee, Address, Role, Signature }

    private sealed class PrivateFormatterLogger(ILogger target) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
            target.BeginScope("Employee input formatter");
        public bool IsEnabled(LogLevel logLevel) => target.IsEnabled(logLevel);
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            // Do not invoke the stock delegate, enumerate input-bearing state, or
            // forward exception objects to any configured provider.
            target.Log(logLevel, eventId, "Employee input formatter diagnostic; exception type {ExceptionType}",
                exception?.GetType().FullName ?? "None");
        }
    }
}

#nullable disable
// Original committed shapes: no EF references, no validation attributes and
// oblivious reference metadata. Role is nested validation only, never a Role API.
internal sealed class OriginalEmployeeInput
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
    public OriginalAddressInput HomeAddress { get; set; }
    public OriginalRoleInput Role { get; set; }
}

internal sealed class OriginalAddressInput
{
    public OriginalAddressInput() => Employee = new HashSet<OriginalEmployeeInput>();
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
    public ICollection<OriginalEmployeeInput> Employee { get; set; }
}

internal sealed class OriginalRoleInput
{
    public OriginalRoleInput() => Employee = new HashSet<OriginalEmployeeInput>();
    public int Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public DateTime? CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public ICollection<OriginalEmployeeInput> Employee { get; set; }
}

internal sealed class OriginalSignatureInput
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string Bucket { get; set; }
    public string ObjectName { get; set; }
    public DateTime? CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
}
#nullable restore

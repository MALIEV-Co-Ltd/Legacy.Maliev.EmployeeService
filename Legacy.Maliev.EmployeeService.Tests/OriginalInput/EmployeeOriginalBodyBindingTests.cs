using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text;
using Legacy.Maliev.EmployeeService.Application.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.EmployeeService.Tests.OriginalInput;

/// <summary>Checks source-shaped body projection ownership, validation suppression scope, and native formatter delegation.</summary>
/// <param name="fixture">The shared production Employee host, PostgreSQL, Redis, and controlled authorization fixture used by these assertions.</param>
[Collection("Employee scaffold runtime")]
public sealed class EmployeeOriginalBodyBindingTests(EmployeeRouteAcceptanceFixture fixture)
    : IClassFixture<EmployeeRouteAcceptanceFixture>
{
    private IServiceProvider Services => fixture.Factory.Services;

    /// <summary>Checks that successful audited projections do not add inferred required validation absent from source-shaped models.</summary>
    /// <param name="kind">The projected body selector: employee, address, or signature.</param>
    /// <param name="nativeInvalid">Whether the current native DTO validation is expected to reject the empty object before audited projection.</param>
    /// <returns>A task that completes after verifying that successful audited projections do not add inferred required validation absent from source-shaped models.</returns>
    [Theory]
    [InlineData("employee", true)]
    [InlineData("address", false)]
    [InlineData("signature", true)]
    public async Task SourceHasNoInferredRequired_ProjectionMustNotAddTargetValidation(string kind, bool nativeInvalid)
    {
        var metadata = Services.GetRequiredService<IModelMetadataProvider>();
        var validator = Services.GetRequiredService<IObjectModelValidator>();
        var source = await EmployeeOriginalInputOracle.ReadAsync(kind, "{}");
        Assert.Equal("success", source.Kind);
        Assert.NotNull(source.Model);
        var sourceMetadata = metadata.GetMetadataForType(EmployeeOriginalInputOracle.SourceType(kind));
        Assert.False(sourceMetadata.HasValidators ?? true);
        var sourceAction = Action();
        validator.Validate(sourceAction, new ValidationStateDictionary(), "", source.Model);
        Assert.True(sourceAction.ModelState.IsValid);
        foreach (var property in EmployeeOriginalInputOracle.SourceType(kind).GetProperties()
            .Where(property => !property.PropertyType.IsValueType))
            Assert.Equal(NullabilityState.Unknown, new NullabilityInfoContext().Create(property).ReadState);

        var native = await Bind(kind, "{}", native: true);
        Assert.True(native.Result.IsModelSet);
        Assert.Empty(native.ValidationState);
        validator.Validate(native.ActionContext, native.ValidationState, "", native.Result.Model);
        Assert.Equal(nativeInvalid, !native.ModelState.IsValid);

        var candidate = await Bind(kind, "{}", native: false);
        var model = candidate.Result.Model;
        Assert.True(candidate.Result.IsModelSet);
        Assert.NotNull(model);
        var entry = Assert.Single(candidate.ValidationState);
        Assert.Same(model, entry.Key);
        Assert.True(entry.Value.SuppressValidation);
        Assert.Equal(candidate.ModelName, entry.Value.Key);
        validator.Validate(candidate.ActionContext, candidate.ValidationState, "", model);
        Assert.True(candidate.ModelState.IsValid);
        Assert.Equal(EmployeeOriginalInputOracle.Projection(source.Model), EmployeeOriginalInputOracle.Projection(model));
        Assert.False(candidate.HttpContext.Items.ContainsKey(EmployeeOriginalBodyNative.ProjectionKey));
    }

    /// <summary>Checks that failed or no-value binding neither suppresses validation nor retains a projection ownership marker.</summary>
    /// <param name="json">The null, empty, malformed, or otherwise non-binding request body.</param>
    /// <param name="contentType">The request media type, including a case with an unsupported charset.</param>
    /// <returns>A task that completes after verifying that failed or no-value binding neither suppresses validation nor retains a projection ownership marker.</returns>
    [Theory]
    [InlineData("null", "application/json")]
    [InlineData("", "application/json")]
    [InlineData("  ", "application/json")]
    [InlineData("{\"CountryId\":{}}", "application/json")]
    [InlineData("{}", "application/json; charset=x-private-marker")]
    public async Task FailedAndNoValueBindings_DoNotSuppressOrRetainOwnership(string json, string contentType)
    {
        var result = await Bind("address", json, native: false, contentType: contentType);
        Assert.False(result.Result.IsModelSet);
        Assert.False(result.ModelState.IsValid);
        Assert.Empty(result.ValidationState);
        Assert.False(result.HttpContext.Items.ContainsKey(EmployeeOriginalBodyNative.ProjectionKey));
    }

    /// <summary>Checks that a successful body projection preserves preexisting model-state errors.</summary>
    /// <returns>A task that completes after verifying that a successful body projection preserves preexisting model-state errors.</returns>
    [Fact]
    public async Task SuccessfulProjection_DoesNotRemovePreexistingErrors()
    {
        var result = await Bind("address", """{"Building":"B","CountryId":7}""", native: false, initialError: true);
        Assert.True(result.Result.IsModelSet);
        Assert.False(result.ModelState.IsValid);
        Assert.Equal("retained", Assert.Single(result.ModelState["fixed"]!.Errors).ErrorMessage);
        Services.GetRequiredService<IObjectModelValidator>()
            .Validate(result.ActionContext, result.ValidationState, "", result.Result.Model);
        Assert.False(result.ModelState.IsValid);
        Assert.Equal("retained", Assert.Single(result.ModelState["fixed"]!.Errors).ErrorMessage);
    }

    /// <summary>Checks stale projection markers are removed before a foreign formatter and foreign success is validated normally.</summary>
    /// <returns>A task that completes after verifying stale projection markers are removed before a foreign formatter and foreign success is validated normally.</returns>
    [Fact]
    public async Task StaleMarkerIsRemovedBeforeForeignFormatter_AndForeignSuccessIsValidatedNormally()
    {
        var model = new UpsertEmployeeRequest(null, null!, null!, null, null!, null, null);
        var probe = new ProbeFormatter(_ => InputFormatterResult.SuccessAsync(model));
        var result = await Bind("employee", "{}", native: false, leading: probe, stale: model);
        Assert.True(result.Result.IsModelSet);
        Assert.Same(model, result.Result.Model);
        Assert.Empty(result.ValidationState);
        Assert.False(result.HttpContext.Items.ContainsKey(EmployeeOriginalBodyNative.ProjectionKey));
        Services.GetRequiredService<IObjectModelValidator>()
            .Validate(result.ActionContext, result.ValidationState, "", model);
        Assert.False(result.ModelState.IsValid);
    }

    /// <summary>Checks that transport exceptions escape while finally cleanup removes the private projection marker.</summary>
    /// <returns>A task that completes after verifying that transport exceptions escape while finally cleanup removes the private projection marker.</returns>
    [Fact]
    public async Task ExceptionFinallyRemovesPrivateMarkerWithoutSwallowingTransportFailure()
    {
        var model = new UpsertEmployeeRequest(null, "N", "L", null, "e@x", null, null);
        var action = Action();
        var probe = new ProbeFormatter(context =>
        {
            context.HttpContext.Items[EmployeeOriginalBodyNative.ProjectionKey] = model;
            throw new IOException("private-stream-marker");
        });
        var options = CandidateOptions();
        options.InputFormatters.Insert(0, probe);
        var context = Binding(action, typeof(UpsertEmployeeRequest), "{}");
        using var body = action.HttpContext.Request.Body;
        var binder = Provider(options).GetBinder(new ProviderContext(context.ModelMetadata,
            Services.GetRequiredService<IModelMetadataProvider>(), BindingSource.Body));
        Assert.NotNull(binder);
        await Assert.ThrowsAsync<IOException>(() => binder.BindModelAsync(context));
        Assert.Empty(context.ValidationState);
        Assert.False(action.HttpContext.Items.ContainsKey(EmployeeOriginalBodyNative.ProjectionKey));
    }

    /// <summary>Checks the body-provider wrapper preserves provider order, wraps the actual delegate, and retains native null delegation.</summary>
    [Fact]
    public void RegistrationWrapsActualBodyProviderInPlace_AndRetainsForeignOrderAndNullDelegation()
    {
        var native = EmployeeOriginalBodyNative.Create(Services);
        var before = native.ModelBinderProviders.ToArray();
        var bodyIndex = Array.FindIndex(before, provider => provider.GetType() == typeof(BodyModelBinderProvider));
        Assert.True(bodyIndex >= 0);
        var original = before[bodyIndex];
        Services.GetServices<IConfigureOptions<MvcOptions>>().Single(EmployeeOriginalBodyNative.IsOwned).Configure(native);
        Assert.Equal(before.Length, native.ModelBinderProviders.Count);
        for (var index = 0; index < before.Length; index++)
        {
            if (index == bodyIndex)
            {
                Assert.Equal(EmployeeOriginalBodyNative.ProviderName, native.ModelBinderProviders[index].GetType().FullName);
                var field = native.ModelBinderProviders[index].GetType()
                    .GetField("inner", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(field);
                Assert.Same(original, field.GetValue(native.ModelBinderProviders[index]));
            }
            else
            {
                Assert.Same(before[index], native.ModelBinderProviders[index]);
            }
        }
        var wrapper = native.ModelBinderProviders[bodyIndex];
        var metadata = Services.GetRequiredService<IModelMetadataProvider>();
        foreach (var type in new[] { typeof(UpsertEmployeeRequest), typeof(UpsertAddressRequest), typeof(UpsertSignatureImageFileRequest) })
        {
            var selected = wrapper.GetBinder(new ProviderContext(metadata.GetMetadataForType(type), metadata, BindingSource.Body));
            Assert.NotNull(selected);
            Assert.Equal(EmployeeOriginalBodyNative.BinderName, selected.GetType().FullName);
            Assert.Null(wrapper.GetBinder(new ProviderContext(metadata.GetMetadataForType(type), metadata, BindingSource.Query)));
        }
        foreach (var type in new[] { typeof(UpsertRoleRequest), typeof(UpdateEmployeeSelfProfileRequest),
            typeof(EmployeeHomeAddressEditRequest), typeof(EmployeeResponse) })
        {
            var unselected = wrapper.GetBinder(new ProviderContext(metadata.GetMetadataForType(type), metadata, BindingSource.Body));
            Assert.IsType<BodyModelBinder>(unselected);
        }
        Assert.Single(native.InputFormatters, item => item.GetType().FullName == EmployeeOriginalBodyNative.FormatterName);
    }

    /// <summary>Checks that validation suppression is confined to audited attribute-free DTOs and their action parameters.</summary>
    [Fact]
    public void SuppressionIsBoundToAuditedAttributeFreeTypesAndActionParameters()
    {
        var types = new[] { typeof(UpsertEmployeeRequest), typeof(UpsertAddressRequest), typeof(UpsertSignatureImageFileRequest) };
        var metadata = Services.GetRequiredService<IModelMetadataProvider>();
        foreach (var type in types)
        {
            Assert.Empty(type.GetCustomAttributes<ValidationAttribute>(true));
            foreach (var property in type.GetProperties())
                Assert.Empty(property.GetCustomAttributes<ValidationAttribute>(true));
            foreach (var parameter in type.GetConstructors().SelectMany(constructor => constructor.GetParameters()))
                Assert.Empty(parameter.GetCustomAttributes<ValidationAttribute>(true));
            var model = metadata.GetMetadataForType(type);
            Assert.Empty(model.ValidatorMetadata);
            foreach (var member in model.Properties.Concat(model.BoundConstructor?.BoundConstructorParameters ?? []))
                Assert.All(member.ValidatorMetadata, value =>
                {
                    var required = Assert.IsType<RequiredAttribute>(value);
                    Assert.True(required.AllowEmptyStrings);
                });
        }
        var parameters = typeof(Program).Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type))
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .SelectMany(method => method.GetParameters()).Where(parameter => types.Contains(parameter.ParameterType)).ToArray();
        Assert.Equal(7, parameters.Length);
        Assert.All(parameters, parameter => Assert.Empty(parameter.GetCustomAttributes<ValidationAttribute>(true)));
    }

    private MvcOptions CandidateOptions()
    {
        var options = EmployeeOriginalBodyNative.Create(Services);
        Services.GetServices<IConfigureOptions<MvcOptions>>().Single(EmployeeOriginalBodyNative.IsOwned).Configure(options);
        return options;
    }

    private IModelBinderProvider Provider(MvcOptions options) => options.ModelBinderProviders.Single(provider =>
        provider.GetType().FullName == EmployeeOriginalBodyNative.ProviderName || provider.GetType() == typeof(BodyModelBinderProvider));

    private async Task<ModelBindingContext> Bind(string kind, string json, bool native,
        string contentType = "application/json", IInputFormatter? leading = null, object? stale = null, bool initialError = false)
    {
        var options = native ? EmployeeOriginalBodyNative.Create(Services) : CandidateOptions();
        if (leading is not null) options.InputFormatters.Insert(0, leading);
        var action = Action();
        var context = Binding(action, EmployeeOriginalInputOracle.DtoType(kind), json, contentType);
        if (stale is not null) action.HttpContext.Items[EmployeeOriginalBodyNative.ProjectionKey] = stale;
        if (initialError) context.ModelState.AddModelError("fixed", "retained");
        var binder = Provider(options).GetBinder(new ProviderContext(context.ModelMetadata,
            Services.GetRequiredService<IModelMetadataProvider>(), BindingSource.Body));
        Assert.NotNull(binder);
        using var body = action.HttpContext.Request.Body;
        await binder.BindModelAsync(context);
        return context;
    }

    private ActionContext Action()
    {
        var http = new DefaultHttpContext { RequestServices = Services };
        return new ActionContext(http, new RouteData(), new ActionDescriptor(), new ModelStateDictionary());
    }

    private ModelBindingContext Binding(ActionContext action, Type type, string json, string contentType = "application/json")
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        action.HttpContext.Request.Body = new MemoryStream(bytes, writable: false);
        action.HttpContext.Request.ContentLength = bytes.LongLength;
        action.HttpContext.Request.ContentType = contentType;
        return DefaultModelBindingContext.CreateBindingContext(action, new CompositeValueProvider(),
            Services.GetRequiredService<IModelMetadataProvider>().GetMetadataForType(type),
            new BindingInfo { BindingSource = BindingSource.Body }, "");
    }

    private sealed class ProviderContext(ModelMetadata metadata, IModelMetadataProvider provider, BindingSource source)
        : ModelBinderProviderContext
    {
        public override BindingInfo BindingInfo { get; } = new() { BindingSource = source };
        public override ModelMetadata Metadata => metadata;
        public override IModelMetadataProvider MetadataProvider => provider;
        public override IModelBinder CreateBinder(ModelMetadata value) =>
            throw new InvalidOperationException("A delegated body provider must not recurse through CreateBinder.");
    }

    private sealed class ProbeFormatter(Func<InputFormatterContext, Task<InputFormatterResult>> callback) : IInputFormatter
    {
        public bool CanRead(InputFormatterContext context) => true;
        public Task<InputFormatterResult> ReadAsync(InputFormatterContext context) => callback(context);
    }
}

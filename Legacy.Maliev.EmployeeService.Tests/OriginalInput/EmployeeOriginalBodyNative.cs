using Legacy.Maliev.EmployeeService.Application.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.EmployeeService.Tests.OriginalInput;

internal static class EmployeeOriginalBodyNative
{
    internal const string SetupName = "Legacy.Maliev.EmployeeService.Api.Serialization.EmployeeOriginalBodyMvcOptionsSetup";
    internal const string FormatterName = "Legacy.Maliev.EmployeeService.Api.Serialization.EmployeeOriginalBodyInputFormatter";
    internal const string ProviderName = "Legacy.Maliev.EmployeeService.Api.Serialization.EmployeeOriginalBodyModelBinderProvider";
    internal const string BinderName = "Legacy.Maliev.EmployeeService.Api.Serialization.EmployeeOriginalBodyModelBinder";
    internal const string ProjectionName = "Legacy.Maliev.EmployeeService.Api.Serialization.EmployeeOriginalBodyProjection";

    internal static bool IsOwned(IConfigureOptions<MvcOptions> value) =>
        value.GetType().Assembly == typeof(Program).Assembly && value.GetType().FullName == SetupName;

    internal static MvcOptions Create(IServiceProvider services)
    {
        var setups = services.GetServices<IConfigureOptions<MvcOptions>>().ToArray();
        Assert.Single(setups, IsOwned);
        var retained = setups.Where(setup => !IsOwned(setup)).ToArray();
        Assert.Equal(setups.Length - 1, retained.Length);
        var options = new OptionsFactory<MvcOptions>(retained,
            services.GetServices<IPostConfigureOptions<MvcOptions>>(),
            services.GetServices<IValidateOptions<MvcOptions>>()).Create(Options.DefaultName);
        Assert.DoesNotContain(options.InputFormatters, formatter => formatter.GetType().FullName == FormatterName);
        Assert.DoesNotContain(options.ModelBinderProviders, provider => provider.GetType().FullName == ProviderName);
        Assert.Single(options.InputFormatters, formatter => formatter is SystemTextJsonInputFormatter);
        return options;
    }

    internal static void RemoveOwned(IServiceCollection services)
    {
        var before = services.ToArray();
        var owned = before.Where(descriptor => descriptor.ServiceType == typeof(IConfigureOptions<MvcOptions>)
            && descriptor.ImplementationType is { } type && type.Assembly == typeof(Program).Assembly
            && type.FullName == SetupName).ToArray();
        Assert.Single(owned);
        Assert.True(services.Remove(owned[0]));
        var retained = before.Where(descriptor => !ReferenceEquals(descriptor, owned[0])).ToArray();
        Assert.Equal(retained.Length, services.Count);
        for (var index = 0; index < retained.Length; index++) Assert.Same(retained[index], services[index]);
    }

    internal static Task<EmployeeBodyRead> ReadAsync(IServiceProvider services, MvcOptions options, string kind,
        byte[] bytes, string contentType = "application/json", string prefix = "", int maxErrors = 200)
    {
        var formatter = options.InputFormatters.First(item =>
            item.GetType().FullName == FormatterName || item is SystemTextJsonInputFormatter);
        return EmployeeOriginalInputOracle.ReadBytesAsync(formatter, EmployeeOriginalInputOracle.DtoType(kind),
            services.GetRequiredService<IModelMetadataProvider>(), bytes, contentType, prefix, maxErrors);
    }

    internal static object ProjectionKey => typeof(Program).Assembly.GetType(ProjectionName, true)!
        .GetField("Key", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
}

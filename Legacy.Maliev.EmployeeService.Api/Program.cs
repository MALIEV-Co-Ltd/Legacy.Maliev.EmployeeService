using System.Text.Json.Serialization;
using Legacy.Maliev.EmployeeService.Api.Documentation;
using Legacy.Maliev.EmployeeService.Application.Interfaces;
using Legacy.Maliev.EmployeeService.Application.Services;
using Legacy.Maliev.EmployeeService.Data;
using Maliev.Aspire.ServiceDefaults;
using Maliev.Aspire.ServiceDefaults.Diagnostics;

try
{
    await RunHostAsync(args);
}
catch (Microsoft.Extensions.Hosting.HostAbortedException)
{
    throw;
}
catch (Exception exception)
{
    PrivateStartupBoundary.ReportFailure(exception);
}

static async Task RunHostAsync(string[] startupArgs)
{
    var builder = WebApplication.CreateBuilder(startupArgs);

    builder.AddServiceDefaults();
    builder.AddDefaultApiVersioning();
    builder.AddPostgresDbContext<EmployeeDbContext>(connectionName: "EmployeeDbContext");
    builder.AddStandardCache("legacy:employee:");
    builder.AddStandardCors();
    builder.AddJwtAuthentication();
    builder.AddStandardMiddleware(options => options.EnableRequestLogging = true);
    builder.AddStandardOpenApi(
        title: "Legacy MALIEV Employee Service API",
        description: "Temporary .NET 10 compatibility service preserving legacy employee, address, role, and signature contracts.");

    builder.Services.AddOpenApi("v1", EmployeeOpenApi.Configure);

    builder.Services.ConfigureHttpJsonOptions(options =>
    {
        options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.SerializerOptions.PropertyNamingPolicy = null;
        options.SerializerOptions.DictionaryKeyPolicy = null;
    });
    builder.Services.AddControllers().AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
        options.JsonSerializerOptions.DictionaryKeyPolicy = null;
    });
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddScoped<IEmployeeRepository, EmployeeRepository>();
    builder.Services.AddScoped<IEmployeeCache, DistributedEmployeeCache>();
    builder.Services.AddScoped<IEmployeeService, EmployeeApplicationService>();

    var app = builder.Build();

    app.UseStandardMiddleware();
    app.UseCors();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapDefaultEndpoints("employee");
    app.MapControllers();
    app.MapApiDocumentation(servicePrefix: "employee");

    await app.RunAsync();
}

/// <summary>Legacy Employee Service entry point.</summary>
public partial class Program;

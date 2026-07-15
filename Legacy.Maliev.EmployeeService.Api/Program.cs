using System.Text.Json.Serialization;
using Legacy.Maliev.EmployeeService.Application.Interfaces;
using Legacy.Maliev.EmployeeService.Application.Services;
using Legacy.Maliev.EmployeeService.Data;
using Maliev.Aspire.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddDefaultApiVersioning();
builder.AddPostgresDbContext<EmployeeDbContext>(connectionName: "EmployeeDbContext");
builder.AddStandardCache("legacy:employee:");
builder.AddStandardCors();
builder.AddJwtAuthentication();
builder.AddStandardMiddleware(options => options.EnableRequestLogging = true);
builder.AddStandardOpenApi(
    title: "Legacy MALIEV Employee Service API",
    description: "Temporary .NET 10 compatibility service preserving legacy employee, address, role, signature, and identity-proxy contracts.");

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.JsonSerializerOptions.PropertyNamingPolicy = null;
    options.JsonSerializerOptions.DictionaryKeyPolicy = null;
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient<IEmployeeIdentityDirectory, AuthServiceEmployeeIdentityDirectory>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["AuthService:LegacyEmployeeIdentityBaseUrl"]
        ?? "http://authservice/auth/v1/legacy/employees/");
    client.Timeout = TimeSpan.FromSeconds(15);
}).AddStandardResilienceHandler();
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

/// <summary>Legacy Employee Service entry point.</summary>
public partial class Program;

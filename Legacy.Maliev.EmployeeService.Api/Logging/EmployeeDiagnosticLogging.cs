using Microsoft.Extensions.Logging.Console;

namespace Legacy.Maliev.EmployeeService.Api.Logging;

/// <summary>Registers Employee's private console diagnostics after the shared service defaults.</summary>
public static class EmployeeDiagnosticLogging
{
    /// <summary>Selects the application-owned formatter and preserves the original console warning filter.</summary>
    /// <param name="builder">The application logging builder.</param>
    /// <returns>The same logging builder.</returns>
    public static ILoggingBuilder AddEmployeePrivateDiagnostics(this ILoggingBuilder builder)
    {
        builder.AddConsoleFormatter<EmployeeDiagnosticConsoleFormatter, ConsoleFormatterOptions>();
        builder.AddConsole(options => options.FormatterName = EmployeeDiagnosticConsoleFormatter.FormatterName);
        builder.AddFilter<ConsoleLoggerProvider>(null, LogLevel.Warning);
        return builder;
    }
}

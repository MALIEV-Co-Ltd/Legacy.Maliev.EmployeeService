using System.Xml.Linq;

namespace Legacy.Maliev.EmployeeService.Tests.Documentation;

/// <summary>Checks compiler-produced Employee XML documentation and its isolation from project source roots.</summary>
public sealed class EmployeeGeneratedDocumentationIsolationTests
{
    /// <summary>Checks actual assembly XML output, maintained member summaries, and absence of source-root artifacts.</summary>
    /// <param name="assemblyName">The Employee assembly name identifying its compiler-generated XML output and project source directory.</param>
    /// <param name="documentedMember">The XML member identifier whose maintained summary must appear in the compiled assembly documentation.</param>
    [Theory]
    [InlineData("Legacy.Maliev.EmployeeService.Api", "T:Program")]
    [InlineData("Legacy.Maliev.EmployeeService.Application", "T:Legacy.Maliev.EmployeeService.Application.Interfaces.IEmployeeService")]
    [InlineData("Legacy.Maliev.EmployeeService.Data", "T:Legacy.Maliev.EmployeeService.Data.EmployeeDbContext")]
    [InlineData("Legacy.Maliev.EmployeeService.Domain", "T:Legacy.Maliev.EmployeeService.Domain.Employee")]
    [InlineData("Legacy.Maliev.EmployeeService.Tests", "T:Legacy.Maliev.EmployeeService.Tests.Documentation.EmployeeGeneratedDocumentationIsolationTests")]
    [InlineData("Legacy.Maliev.EmployeeService.Tests", "M:Legacy.Maliev.EmployeeService.Tests.Documentation.EmployeeGeneratedDocumentationIsolationTests.CompilerDocumentation_IsAvailableInOutputWithoutSourceRootArtifact(System.String,System.String)")]
    [InlineData("Legacy.Maliev.EmployeeService.Tests", "P:Legacy.Maliev.EmployeeService.Tests.EmployeeRouteAcceptanceFixture.Factory")]
    public void CompilerDocumentation_IsAvailableInOutputWithoutSourceRootArtifact(string assemblyName, string documentedMember)
    {
        var output = Path.Combine(AppContext.BaseDirectory, assemblyName + ".xml");
        Assert.True(File.Exists(output), "Build the actual Employee solution projects before documentation acceptance.");

        var document = XDocument.Load(output);
        Assert.Equal(assemblyName, document.Root?.Element("assembly")?.Element("name")?.Value);
        var member = Assert.Single(document.Descendants("member"), element =>
            string.Equals((string?)element.Attribute("name"), documentedMember, StringComparison.Ordinal));
        Assert.False(string.IsNullOrWhiteSpace(member.Element("summary")?.Value));

        var sourceArtifact = Path.Combine(FindRepositoryRoot(), assemblyName, assemblyName + ".xml");
        Assert.False(File.Exists(sourceArtifact), "Generated assembly documentation must remain outside the project source root.");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.EmployeeService.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Employee repository root.");
    }
}

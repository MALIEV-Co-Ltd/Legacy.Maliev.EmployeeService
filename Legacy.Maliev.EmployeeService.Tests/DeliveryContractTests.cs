namespace Legacy.Maliev.EmployeeService.Tests;

public sealed class DeliveryContractTests
{
    [Fact]
    public void DockerContext_ExcludesBuildRepositoryAndSecretArtifacts()
    {
        var dockerIgnore = File.ReadAllText(Path.Combine(FindRoot(), ".dockerignore"));

        Assert.Contains(".git", dockerIgnore, StringComparison.Ordinal);
        Assert.Contains(".worktrees", dockerIgnore, StringComparison.Ordinal);
        Assert.Contains(".dependencies", dockerIgnore, StringComparison.Ordinal);
        Assert.Contains("**/bin", dockerIgnore, StringComparison.Ordinal);
        Assert.Contains("**/obj", dockerIgnore, StringComparison.Ordinal);
        Assert.Contains("TestResults", dockerIgnore, StringComparison.Ordinal);
        Assert.Contains("*.pfx", dockerIgnore, StringComparison.Ordinal);
        Assert.Contains("*.key", dockerIgnore, StringComparison.Ordinal);
        Assert.Contains("*.pem", dockerIgnore, StringComparison.Ordinal);
        Assert.Contains(".env", dockerIgnore, StringComparison.Ordinal);
    }

    [Fact]
    public void KubernetesResources_AreNamespaceConfinedAndProvisionNoInfrastructure()
    {
        var manifests = Directory.GetFiles(Path.Combine(FindRoot(), "deploy", "base"), "*.yaml");
        var combined = string.Join('\n', manifests.Select(File.ReadAllText));

        Assert.NotEmpty(manifests);
        Assert.DoesNotContain("kind: Cluster", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("kind: NodePool", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("CloudSQL", combined, StringComparison.OrdinalIgnoreCase);
        Assert.All(
            manifests.Where(path => !path.EndsWith("kustomization.yaml", StringComparison.Ordinal)),
            path => Assert.Contains("namespace: maliev-legacy", File.ReadAllText(path), StringComparison.Ordinal));
    }

    [Fact]
    public void Deployment_IsSmallNonRootAndUsesRuntimeSecretProjection()
    {
        var deployment = File.ReadAllText(Path.Combine(FindRoot(), "deploy", "base", "deployment.yaml"));

        Assert.Contains("replicas: 1", deployment, StringComparison.Ordinal);
        Assert.Contains("runAsNonRoot: true", deployment, StringComparison.Ordinal);
        Assert.Contains("readOnlyRootFilesystem: true", deployment, StringComparison.Ordinal);
        Assert.Contains("drop: [\"ALL\"]", deployment, StringComparison.Ordinal);
        Assert.Contains("name: legacy-maliev-employee-runtime", deployment, StringComparison.Ordinal);
        Assert.Contains("path: /employee/readiness", deployment, StringComparison.Ordinal);
        Assert.Contains("path: /employee/liveness", deployment, StringComparison.Ordinal);
        Assert.Contains("cpu: 50m", deployment, StringComparison.Ordinal);
        Assert.DoesNotContain("AuthService__LegacyEmployeeIdentityBaseUrl", deployment, StringComparison.Ordinal);
    }

    [Fact]
    public void PublishWorkflow_IsExplicitlyGatedAndNeverAppliesManifests()
    {
        var workflow = File.ReadAllText(Path.Combine(FindRoot(), ".github", "workflows", "publish-image.yml"));

        Assert.Contains("vars.LEGACY_DEPLOY_ENABLED == 'true'", workflow, StringComparison.Ordinal);
        Assert.Contains("Legacy.Maliev.Workflows/.github/workflows/publish-image.yml@6017816", workflow, StringComparison.Ordinal);
        Assert.Contains("legacy-maliev-employee-service", workflow, StringComparison.Ordinal);
        Assert.Contains("legacy-production", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("kubectl apply", workflow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dockerfile_IsDotNet10NonRootAndPinsPublicLegacyDependencies()
    {
        var dockerfile = File.ReadAllText(Path.Combine(FindRoot(), "Legacy.Maliev.EmployeeService.Api", "Dockerfile"));

        Assert.Contains("dotnet/sdk:10.0-alpine", dockerfile, StringComparison.Ordinal);
        Assert.Contains("dotnet/aspnet:10.0-alpine", dockerfile, StringComparison.Ordinal);
        Assert.Contains("USER $APP_UID", dockerfile, StringComparison.Ordinal);
        Assert.Contains("Legacy.Maliev.ServiceDefaults.git", dockerfile, StringComparison.Ordinal);
        Assert.Contains("checkout bcab875a7f703d1d9c2d535479e93653720eb62d", dockerfile, StringComparison.Ordinal);
        Assert.Contains("Legacy.Maliev.CompatibilityContracts.git", dockerfile, StringComparison.Ordinal);
        Assert.Contains("checkout 95c62eb6209411f5aada443b315447a2f76ca0cd", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("COPY .dependencies/", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("MALIEV-Co-Ltd/Maliev.Aspire", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("MALIEV-Co-Ltd/Maliev.MessagingContracts", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void DeploymentContract_ListsExactRuntimeSecretAndRollbackPrerequisites()
    {
        var contract = File.ReadAllText(Path.Combine(FindRoot(), "deploy", "README.md"));

        Assert.Contains("ConnectionStrings__EmployeeDbContext", contract, StringComparison.Ordinal);
        Assert.Contains("ConnectionStrings__redis", contract, StringComparison.Ordinal);
        Assert.Contains("Jwt__PublicKey", contract, StringComparison.Ordinal);
        Assert.Contains("Jwt__Issuer", contract, StringComparison.Ordinal);
        Assert.Contains("Jwt__Audience", contract, StringComparison.Ordinal);
        Assert.Contains("previous immutable image digest", contract, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("source SQL Server", contract, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rollback", contract, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Jwt__PrivateKey", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("AuthService__LegacyEmployeeIdentityBaseUrl", contract, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.EmployeeService.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}

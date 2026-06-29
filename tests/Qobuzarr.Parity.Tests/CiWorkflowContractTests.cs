using Xunit;

namespace Qobuzarr.Parity.Tests;

[Trait("Category", "Parity")]
public class CiWorkflowContractTests
{
    [Fact]
    public void GiteaCiWorkflow_RunsSecretScan()
    {
        var workflow = File.ReadAllText(Path.Combine(FindRepositoryRoot(), ".gitea", "workflows", "ci.yml"));

        Assert.Matches(@"(?m)^  secret-scan:\s*$", workflow);
        Assert.Matches(@"(?ms)^  verify:\s*\r?\n(?:    .*\r?\n)*?    needs:\s*\[lint,\s*secret-scan\]\s*$", workflow);
        Assert.Contains("sha256sum -c -", workflow);
        Assert.Contains("/tmp/gitleaks detect --source . --no-banner --redact --exit-code 1", workflow);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, ".gitea", "workflows", "ci.yml")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate repository root from {AppContext.BaseDirectory}");
    }
}

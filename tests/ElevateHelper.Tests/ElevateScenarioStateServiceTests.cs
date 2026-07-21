using ElevateHelperWinUI.Services;

namespace ElevateHelper.Tests;

public sealed class ElevateScenarioStateServiceTests
{
    [Fact]
    public void IsCurrent_ReturnsFalseAfterManagedProjectChanges()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "ElevateHelperScenarioStateTests",
            Guid.NewGuid().ToString("N"));
        string scenarioPath = Path.Combine(root, "morning");
        Directory.CreateDirectory(scenarioPath);

        try
        {
            string sourcePath = Path.Combine(root, "Project01.elvx");
            string managedPath = Path.Combine(scenarioPath, "Project01.elvx");
            File.WriteAllText(sourcePath, "<Project Source=\"true\" />");
            File.WriteAllText(managedPath, "<Project Scenario=\"true\" />");

            ElevateScenarioStateService service = new();
            ElevateScenarioFingerprint fingerprint = service.CreateFingerprint(
                sourcePath,
                "Project01.elvx",
                "Morning",
                copiesCount: 1);
            service.Save(scenarioPath, fingerprint, ["Project01.elvx"]);

            Assert.True(service.IsCurrent(scenarioPath, fingerprint));

            File.AppendAllText(managedPath, "<!-- changed -->");

            Assert.False(service.IsCurrent(scenarioPath, fingerprint));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

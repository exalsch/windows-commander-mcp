using WindowsCommander.Core.Safety;
using WindowsCommander.Safety.Policy;

namespace WindowsCommander.Tests;

public class RiskPolicyServiceTests
{
    [Theory]
    [InlineData("list_processes", RiskLevel.Low, false)]
    [InlineData("mouse_action", RiskLevel.Medium, false)]
    [InlineData("write_file", RiskLevel.High, true)]
    public void Classify_ReturnsExpectedRisk(string toolName, RiskLevel expectedRisk, bool expectedConfirmation)
    {
        var service = new RiskPolicyService();
        var arguments = new Dictionary<string, object?>();

        var risk = service.Classify(toolName, arguments);
        var requiresConfirmation = service.RequiresConfirmation(toolName, arguments);

        Assert.Equal(expectedRisk, risk);
        Assert.Equal(expectedConfirmation, requiresConfirmation);
    }
}
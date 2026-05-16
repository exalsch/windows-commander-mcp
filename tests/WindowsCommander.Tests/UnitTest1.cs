using WindowsCommander.Core.Safety;
using WindowsCommander.Safety.Policy;

namespace WindowsCommander.Tests;

public class RiskPolicyServiceTests
{
    [Theory]
    [InlineData("list_processes", RiskLevel.Low, false)]
    [InlineData("read_file", RiskLevel.Low, false)]
    [InlineData("not_a_real_tool", RiskLevel.Low, false)]
    [InlineData("mouse_action", RiskLevel.Medium, false)]
    [InlineData("focus_window", RiskLevel.Medium, false)]
    [InlineData("write_file", RiskLevel.High, true)]
    [InlineData("manage_process", RiskLevel.High, true)]
    [InlineData("copy_move_delete_path", RiskLevel.High, true)]
    [InlineData("set_environment_variable", RiskLevel.High, true)]
    [InlineData("execute_powershell", RiskLevel.High, true)]
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
using WindowsCommander.Core.Safety;

namespace WindowsCommander.Safety.Policy;

public sealed class RiskPolicyService : IRiskPolicyService
{
    private static readonly HashSet<string> HighRiskTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "manage_process",
        "write_file",
        "copy_move_delete_path",
        "set_environment_variable",
        "execute_powershell",
        "execute_process"
    };

    private static readonly HashSet<string> MediumRiskTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "open_path",
        "launch_app",
        "mouse_action",
        "type_text",
        "send_hotkey",
        "keyboard_action",
        "mouse_wheel",
        "set_cursor_position",
        "input_sequence",
        "invoke_ui_element",
        "set_ui_value",
        "focus_window",
        "move_resize_window",
        "set_window_state"
    };

    public RiskLevel Classify(string toolName, IReadOnlyDictionary<string, object?> arguments)
    {
        if (HighRiskTools.Contains(toolName))
        {
            return RiskLevel.High;
        }

        if (MediumRiskTools.Contains(toolName))
        {
            return RiskLevel.Medium;
        }

        return RiskLevel.Low;
    }

    public bool RequiresConfirmation(string toolName, IReadOnlyDictionary<string, object?> arguments)
    {
        return Classify(toolName, arguments) == RiskLevel.High;
    }
}

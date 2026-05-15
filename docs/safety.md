# Safety and Permissions

Windows Commander MCP is intended to automate a real Windows desktop, so safety is a first-class design concern.

## Current Safety Features

### Audit Logging

Each tool call dispatched through the MCP server records:

- Operation ID
- Tool name
- Start time
- End time
- Status
- Redacted arguments
- Error summary, when applicable

Audit history is currently in-memory and available through `get_operation_history`.

### Sensitive Argument Redaction

When sensitive arguments are hidden, keys containing these terms are redacted:

- `password`
- `token`
- `secret`
- `key`

### Risk Classification

The `RiskPolicyService` currently classifies known tools as low, medium, or high risk.

High-risk tools include:

- `manage_process`
- `write_file`
- `copy_move_delete_path`
- `set_environment_variable`
- `execute_powershell`
- `execute_process`

Medium-risk tools include computer-control, UI automation, shell launch, and window mutation actions.

## Pending Safety Features

‼️ These planned safety features are not implemented yet:

- Modal user confirmation enforcement
- WPF visual control indicators
- Audio control cues
- Indicator configuration tools
- Persistent audit history
- Policy configuration from file
- Per-tool/action/path confirmation rules

## Recommended Operating Practices

- Run the MCP server as a normal user unless elevation is explicitly required.
- Avoid running against elevated target apps from a non-elevated server; Windows may block access.
- Treat `execute_powershell`, `execute_process`, `write_file`, and `copy_move_delete_path` as high-impact tools.
- Prefer read-only discovery tools before mutation tools.
- Review `get_operation_history` when debugging or auditing behavior.

## Future Policy Direction

The intended safety pipeline is:

1. Validate tool arguments.
2. Classify operation risk.
3. Apply configurable confirmation policy.
4. Show visual/acoustic control indicators when relevant.
5. Execute the operation.
6. Normalize result/errors.
7. Record an audit entry.

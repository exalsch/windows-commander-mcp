# Testing Guide

This project uses automated tests plus manual Windows desktop validation.

## Automated Tests

Run all tests:

```powershell
dotnet test WindowsCommander.slnx
```

Run build and tests:

```powershell
dotnet build WindowsCommander.slnx
dotnet test WindowsCommander.slnx --no-build
```

Current automated tests cover:

- Risk classification
- Audit redaction
- File write/read/hash behavior
- Directory listing behavior

## Manual Test Cases

### TC1 - MCP Initialize

**Goal:** Verify the server responds to MCP initialization.

1. Run the server:

   ```powershell
   dotnet run --project src/WindowsCommander.McpServer/WindowsCommander.McpServer.csproj
   ```

2. Send:

   ```json
   {"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}
   ```

3. Expected result:

   - Response includes `serverInfo.name` as `windows-commander-mcp`.
   - Response includes `capabilities.tools`.

### TC2 - Tool Listing

**Goal:** Verify implemented tools are discoverable.

1. Send:

   ```json
   {"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}
   ```

2. Expected result:

   - Response contains implemented tool names such as `list_processes`, `get_system_info`, `read_file`, and `search_files`.

### TC3 - System Info Tool

**Goal:** Verify a read-only tool call works.

1. Send:

   ```json
   {"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"get_system_info","arguments":{}}}
   ```

2. Expected result:

   - Response content contains OS version, machine name, current user, locale, timezone, and active session state.

### TC4 - File Round Trip

**Goal:** Verify file write/read behavior through MCP.

1. Call `write_file` with a temporary path, `content`, `overwrite: false`, and `create_directories: true`.
2. Call `read_file` for the same path.
3. Expected result:

   - `write_file` reports bytes written.
   - `read_file` returns the same content.

### TC5 - Audit History

**Goal:** Verify audit logging records calls.

1. Run a simple read-only tool such as `get_system_info`.
2. Call `get_operation_history` with `limit: 5`.
3. Expected result:

   - History contains recent operation IDs, tool names, timestamps, and status values.

### TC6 - File Hash

**Goal:** Verify file property hashing.

1. Create a temporary text file.
2. Call `get_file_properties` with `hash_algorithm: "SHA256"`.
3. Expected result:

   - Response includes `HashAlgorithm` as `SHA256`.
   - Response includes a non-empty hash string.

## Manual Test Notes

- Use temporary test paths to avoid accidental data loss.
- Avoid `copy_move_delete_path` on important data until confirmation policies are implemented.
- Elevated apps may behave differently from non-elevated apps.

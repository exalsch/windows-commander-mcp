using System.IO;
using System.Text;
using System.Text.Json;
using WindowsCommander.McpServer.Mcp;
using WindowsCommander.Safety.Audit;
using WindowsCommander.Safety.Policy;
using WindowsCommander.Windows.Services;
using WindowsCommander.McpServer;

// High-risk tools are gated behind a local confirmation dialog by default.
// Setting WINDOWS_COMMANDER_UNATTENDED=1 disables the gate for automated
// harness/CI runs that cannot answer a dialog.
var requireConfirmation = !IsUnattended();

var dispatcher = new ToolDispatcher(
    new ProcessService(),
    new WindowService(),
    new ScreenService(),
    new SystemInfoService(),
    new ExecutionService(),
    new EnvironmentService(),
    new ClipboardService(),
    new FileSystemService(),
    new ShellService(),
    new WindowsServiceDiscoveryService(),
    new RegistryService(),
    new ApplicationService(),
    new InputService(),
    new VisionService(),
    new UiAutomationService(),
    new ControlIndicatorService(),
    new InMemoryAuditLog(),
    new RiskPolicyService(),
    requireConfirmation);

// The MCP stdio transport is strictly UTF-8. Bind explicit UTF-8 (no BOM)
// streams so non-ASCII characters survive regardless of the host code page;
// the default Console streams decode with the OEM code page and corrupt them.
var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
using var input = new StreamReader(Console.OpenStandardInput(), utf8);
await using var output = new StreamWriter(Console.OpenStandardOutput(), utf8)
{
    AutoFlush = false,
    NewLine = "\n"
};

while (await input.ReadLineAsync() is { } line)
{
    if (string.IsNullOrWhiteSpace(line))
    {
        continue;
    }

    JsonRpcRequest? request = null;
    JsonRpcResponse response;

    try
    {
        request = JsonSerializer.Deserialize<JsonRpcRequest>(line, JsonOptions.Default)
            ?? throw new InvalidOperationException("Request body is empty.");

        // JSON-RPC notifications omit "id" (e.g. "notifications/initialized").
        // They require no action here and the server must never send a reply.
        if (request.Id is null)
        {
            continue;
        }

        response = request.Method switch
        {
            "initialize" => JsonRpcResponse.Success(request.Id, new
            {
                protocolVersion = NegotiateProtocolVersion(request.Params),
                capabilities = new
                {
                    tools = new { }
                },
                serverInfo = new
                {
                    name = ServerInfo.Name,
                    version = ServerInfo.Version
                }
            }),
            "tools/list" => JsonRpcResponse.Success(request.Id, dispatcher.ListTools()),
            "tools/call" => JsonRpcResponse.Success(request.Id, await dispatcher.CallToolAsync(
                GetRequiredString(request.Params, "name"),
                GetProperty(request.Params, "arguments"),
                CancellationToken.None)),
            _ => JsonRpcResponse.Failure(request.Id, -32601, $"Method not found: {request.Method}")
        };
    }
    catch (JsonException exception)
    {
        response = JsonRpcResponse.Failure(request?.Id, -32700, exception.Message);
    }
    catch (Exception exception)
    {
        // A single failing request must not crash the server and drop the
        // connection. Invalid arguments map to -32602, anything else to -32603.
        var code = exception is ArgumentException or InvalidOperationException ? -32602 : -32603;
        response = JsonRpcResponse.Failure(request?.Id, code, exception.Message);
    }

    await output.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions.Default));
    await output.FlushAsync();
}

static bool IsUnattended()
{
    var value = Environment.GetEnvironmentVariable("WINDOWS_COMMANDER_UNATTENDED");
    return value is "1" or "true" or "TRUE" or "True" or "yes";
}

static string NegotiateProtocolVersion(JsonElement? requestParams)
{
    // Echo the client's requested protocol version so the handshake always
    // agrees; fall back to the baseline version for clients that omit it.
    const string baseline = "2024-11-05";
    var requested = GetProperty(requestParams, "protocolVersion");

    return requested is { ValueKind: JsonValueKind.String }
        ? requested.Value.GetString() ?? baseline
        : baseline;
}

static string GetRequiredString(JsonElement? element, string propertyName)
{
    var property = GetProperty(element, propertyName);

    return property is not null && property.Value.ValueKind == JsonValueKind.String
        ? property.Value.GetString() ?? string.Empty
        : throw new ArgumentException($"Missing or invalid string property: {propertyName}");
}

static JsonElement? GetProperty(JsonElement? element, string propertyName)
{
    return element is not null
        && element.Value.ValueKind == JsonValueKind.Object
        && element.Value.TryGetProperty(propertyName, out var property)
            ? property
            : null;
}

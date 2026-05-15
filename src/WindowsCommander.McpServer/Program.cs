using System.Text.Json;
using WindowsCommander.McpServer.Mcp;
using WindowsCommander.Safety.Audit;
using WindowsCommander.Windows.Services;

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
    new InMemoryAuditLog());

while (await Console.In.ReadLineAsync() is { } line)
{
    if (string.IsNullOrWhiteSpace(line))
    {
        continue;
    }

    JsonRpcResponse response;

    try
    {
        var request = JsonSerializer.Deserialize<JsonRpcRequest>(line, JsonOptions.Default)
            ?? throw new InvalidOperationException("Request body is empty.");

        response = request.Method switch
        {
            "initialize" => JsonRpcResponse.Success(request.Id, new
            {
                protocolVersion = "2024-11-05",
                capabilities = new
                {
                    tools = new { }
                },
                serverInfo = new
                {
                    name = "windows-commander-mcp",
                    version = "0.1.0"
                }
            }),
            "tools/list" => JsonRpcResponse.Success(request.Id, dispatcher.ListTools()),
            "tools/call" => JsonRpcResponse.Success(request.Id, await dispatcher.CallToolAsync(
                GetRequiredString(request.Params, "name"),
                GetProperty(request.Params, "arguments"),
                CancellationToken.None)),
            "notifications/initialized" => JsonRpcResponse.Success(request.Id, null),
            _ => JsonRpcResponse.Failure(request.Id, -32601, $"Method not found: {request.Method}")
        };
    }
    catch (JsonException exception)
    {
        response = JsonRpcResponse.Failure(null, -32700, exception.Message);
    }
    catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
    {
        response = JsonRpcResponse.Failure(null, -32602, exception.Message);
    }

    await Console.Out.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions.Default));
    await Console.Out.FlushAsync();
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

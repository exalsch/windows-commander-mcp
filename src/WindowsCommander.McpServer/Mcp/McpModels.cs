using System.Text.Json;
using System.Text.Json.Serialization;

namespace WindowsCommander.McpServer.Mcp;

public sealed record JsonRpcRequest(
    [property: JsonPropertyName("jsonrpc")] string? JsonRpc,
    [property: JsonPropertyName("id")] JsonElement? Id,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("params")] JsonElement? Params);

public sealed record JsonRpcResponse(
    [property: JsonPropertyName("jsonrpc")] string JsonRpc,
    [property: JsonPropertyName("id")] JsonElement? Id,
    // A JSON-RPC response carries exactly one of "result" or "error". The unused
    // member is omitted entirely rather than serialized as null. This relies on a
    // successful result never being null (see JsonRpcResponse.Success callers).
    [property: JsonPropertyName("result")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] object? Result,
    [property: JsonPropertyName("error")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonRpcError? Error)
{
    public static JsonRpcResponse Success(JsonElement? id, object? result)
    {
        return new JsonRpcResponse("2.0", id, result, null);
    }

    public static JsonRpcResponse Failure(JsonElement? id, int code, string message)
    {
        return new JsonRpcResponse("2.0", id, null, new JsonRpcError(code, message));
    }
}

public sealed record JsonRpcError(
    [property: JsonPropertyName("code")] int Code,
    [property: JsonPropertyName("message")] string Message);

public sealed record McpTool(string Name, string Description, object InputSchema);

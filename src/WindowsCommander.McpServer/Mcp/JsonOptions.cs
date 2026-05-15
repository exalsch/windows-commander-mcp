using System.Text.Json;

namespace WindowsCommander.McpServer.Mcp;

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };
}

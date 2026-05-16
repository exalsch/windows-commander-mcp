using System.Text.Json;
using System.Text.Json.Serialization;

namespace WindowsCommander.McpServer.Mcp;

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = CreateDefault();

    private static JsonSerializerOptions CreateDefault()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = false
        };

        options.Converters.Add(new NativeIntJsonConverter());

        return options;
    }
}

/// <summary>
/// Serializes native handles (<see cref="nint"/>, e.g. window/process handles) as
/// JSON numbers. System.Text.Json refuses <see cref="System.IntPtr"/> by default,
/// which would otherwise fail any tool result carrying a handle.
/// </summary>
internal sealed class NativeIntJsonConverter : JsonConverter<nint>
{
    public override nint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return (nint)reader.GetInt64();
    }

    public override void Write(Utf8JsonWriter writer, nint value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue((long)value);
    }
}

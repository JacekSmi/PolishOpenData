using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ModelContextProtocol.Protocol;

namespace PolishOpenData.Mcp;

internal static class ToolResults
{
    public static CallToolResult Ok<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        var element = JsonSerializer.SerializeToElement(value, typeInfo);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = element.GetRawText() }],
            StructuredContent = element,
        };
    }

    public static CallToolResult Error(string message) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = message }],
    };
}

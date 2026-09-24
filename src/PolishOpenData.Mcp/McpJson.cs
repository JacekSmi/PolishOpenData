using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PolishOpenData.Mcp;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(IdentifierValidationReport))]
internal sealed partial class McpJsonContext : JsonSerializerContext;

internal static class McpJson
{
    /// <summary>
    /// Output context that keeps Polish letters readable (no \u escapes) for the model. Nulls are written: the
    /// advertised outputSchema marks nullable positional record members as required.
    /// </summary>
    public static McpJsonContext Relaxed { get; } = new(new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });
}

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PolishOpenData.Krs;

/// <summary>Base of all KRS wire models: keeps fields the API added after this library was written.</summary>
public abstract class KrsNode
{
    /// <summary>Fields returned by the API that this library does not model (normally empty).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownFields { get; set; }
}

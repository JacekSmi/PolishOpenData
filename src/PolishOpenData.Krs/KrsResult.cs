using System.Text.Json.Serialization;

namespace PolishOpenData.Krs;

/// <summary>The two KRS registers searched by this client.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<KrsRegister>))]
public enum KrsRegister
{
    /// <summary>Register of entrepreneurs (<i>rejestr przedsiębiorców</i>, code P): companies, partnerships, cooperatives.</summary>
    Entrepreneurs,

    /// <summary>Register of associations and foundations (<i>rejestr stowarzyszeń…</i>, code S).</summary>
    Associations,
}

/// <summary>Outcome of a KRS lookup.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<KrsLookupStatus>))]
public enum KrsLookupStatus
{
    /// <summary>The entity exists and an extract was returned.</summary>
    Found,

    /// <summary>No entity with this number in the searched register(s).</summary>
    NotFound,

    /// <summary>The entity was removed from KRS (<i>wykreślony</i>); the API returns no current extract.</summary>
    Removed,
}

/// <summary>Result of a KRS extract lookup.</summary>
/// <typeparam name="T">The extract type.</typeparam>
public sealed class KrsResult<T>
    where T : class
{
    internal KrsResult(KrsNumber krs, KrsLookupStatus status, KrsRegister? register, T? extract)
    {
        Krs = krs;
        Status = status;
        Register = register;
        Extract = extract;
    }

    /// <summary>The number that was looked up.</summary>
    public KrsNumber Krs { get; }

    /// <summary>Found, NotFound or Removed.</summary>
    public KrsLookupStatus Status { get; }

    /// <summary>The register where the entity was found or removed; <c>null</c> when not found.</summary>
    public KrsRegister? Register { get; }

    /// <summary>The extract when <see cref="Status"/> is <see cref="KrsLookupStatus.Found"/>.</summary>
    public T? Extract { get; }
}

using System;

namespace PolishOpenData.Bir;

/// <summary>
/// A GUS REGON answer together with where and when it came from. GUS asks those who reuse its data to name the source, the
/// time the information was obtained and the fact that it was processed: <see cref="Environment"/> names the source service,
/// <see cref="RetrievedAt"/> is the time this client assembled the result, <see cref="DataAsOf"/> is the date of the register
/// data when it is known, and every value was mapped from GUS's XML by this library.
/// </summary>
/// <typeparam name="T">The payload: the rows, the details, the records or the date.</typeparam>
public sealed class RegonResult<T>
{
    internal RegonResult(T value, RegonEnvironment environment, DateTimeOffset retrievedAt, DateOnly? dataAsOf)
    {
        Value = value;
        Environment = environment;
        RetrievedAt = retrievedAt;
        DataAsOf = dataAsOf;
    }

    /// <summary>The answer.</summary>
    public T Value { get; }

    /// <summary>
    /// Which GUS service answered. <see cref="RegonEnvironment.Test"/> data is an old anonymised copy of the register
    /// and must never be mistaken for the real thing.
    /// </summary>
    public RegonEnvironment Environment { get; }

    /// <summary>The time on this client's clock when the result was assembled, which is when the data were obtained from GUS.</summary>
    public DateTimeOffset RetrievedAt { get; }

    /// <summary>
    /// The date of the register data (<c>StanDanych</c>), or <c>null</c> while it is not known. It is known once
    /// <see cref="IRegonClient.GetDataDateAsync"/> has been called through any client that shares the session; it is never
    /// fetched implicitly, because that would cost a request in every call.
    /// </summary>
    public DateOnly? DataAsOf { get; }
}

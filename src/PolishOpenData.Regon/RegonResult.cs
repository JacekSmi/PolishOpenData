using System;

namespace PolishOpenData.Bir;

/// <summary>
/// An answer from the GUS REGON register together with where and when it came from. GUS asks re-users of its data to
/// state the source, the time the information was created and obtained, and that it was processed: the source is the
/// REGON register (GUS, service BIR), <see cref="DataAsOf"/> is when the data of GUS was current, <see cref="RetrievedAt"/>
/// is when this library obtained it, and the library maps the fields of GUS into the types of this package.
/// </summary>
/// <typeparam name="T">The kind of value.</typeparam>
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

    /// <summary>When this library assembled the result, by the clock it was given (UTC on <see cref="TimeProvider.System"/>).</summary>
    public DateTimeOffset RetrievedAt { get; }

    /// <summary>
    /// The date of the GUS data (<c>StanDanych</c>), or <c>null</c> when it is not known. It is known only after
    /// <c>IRegonClient.GetDataDateAsync</c> has been called by a client that shares the session of this one; the
    /// library never fetches it on its own, because that would cost a request in every call.
    /// </summary>
    public DateOnly? DataAsOf { get; }
}

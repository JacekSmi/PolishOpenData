using System;
using System.Collections.Generic;

namespace PolishOpenData.BialaLista;

/// <summary>An error reported for one identifier inside a batch response.</summary>
public sealed class BialaListaError
{
    internal BialaListaError(string code, string message)
    {
        Code = code;
        Message = message;
    }

    /// <summary>Error code, e.g. <c>WL-115</c> (invalid NIP).</summary>
    public string Code { get; }

    /// <summary>Error message (Polish).</summary>
    public string Message { get; }
}

/// <summary>Result for one identifier of a batch request. Entries are not returned in request order: match on <see cref="Identifier"/>.</summary>
public sealed class VatBatchEntry
{
    internal VatBatchEntry(string identifier, IReadOnlyList<VatSubject> subjects, BialaListaError? error)
    {
        Identifier = identifier;
        Subjects = subjects;
        Error = error;
    }

    /// <summary>The identifier as sent (NIP, REGON or account number).</summary>
    public string Identifier { get; }

    /// <summary>Matching taxpayers; empty when not found or on error.</summary>
    public IReadOnlyList<VatSubject> Subjects { get; }

    /// <summary>Error for this identifier, if any.</summary>
    public BialaListaError? Error { get; }
}

/// <summary>
/// A whitelist answer with the request identifier. Keep <see cref="RequestId"/> and <see cref="RequestDateTime"/>: they
/// document when the whitelist was checked and what it answered, and one result always corresponds to exactly one upstream
/// request.
/// </summary>
/// <typeparam name="T">The answer type.</typeparam>
public sealed class BialaListaResult<T>
{
    internal BialaListaResult(T value, string requestId, DateTimeOffset requestDateTime, IReadOnlyList<string> unknownFields)
    {
        Value = value;
        RequestId = requestId;
        RequestDateTime = requestDateTime;
        UnknownFields = unknownFields;
    }

    /// <summary>The answer.</summary>
    public T Value { get; }

    /// <summary>Upstream request identifier (<c>requestId</c>), e.g. <c>MVRqg-98jk3i1</c>.</summary>
    public string RequestId { get; }

    /// <summary>When the ministry answered (<c>requestDateTime</c>, Warsaw time with offset).</summary>
    public DateTimeOffset RequestDateTime { get; }

    /// <summary>
    /// Envelope fields this library does not model (normally empty). Fields of the taxpayer itself are reported in
    /// <see cref="VatSubject.UnknownFields"/>.
    /// </summary>
    public IReadOnlyList<string> UnknownFields { get; }
}

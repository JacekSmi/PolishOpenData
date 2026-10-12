using System;
using System.Collections.Generic;

namespace PolishOpenData.Bir;

/// <summary>
/// One row of a raw report: the fields of GUS by name. It is the way to reach a field that the typed models leave out.
/// Only fields that GUS filled are present. Field names are compared exactly (case-sensitive), and a few are misspelt
/// in the schema of GUS itself (for example <c>lokfiz_numerwRejestrzeEwidencji</c>); use them as published. GUS warns
/// that free text can contain HTML or SQL-like text; the library does not sanitise it.
/// </summary>
public sealed class RegonRecord
{
    internal RegonRecord(IReadOnlyDictionary<string, string> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        Fields = fields;
    }

    /// <summary>The fields GUS filled, by name. Empty fields are not present.</summary>
    public IReadOnlyDictionary<string, string> Fields { get; }

    /// <summary>The value of a field, or <c>null</c> when GUS sent nothing for it.</summary>
    /// <param name="fieldName">The exact GUS field name, for example <c>praw_numerTelefonu</c>.</param>
    public string? this[string fieldName] => Fields.TryGetValue(fieldName, out var value) ? value : null;
}

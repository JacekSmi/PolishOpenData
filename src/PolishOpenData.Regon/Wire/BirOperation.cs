using System;

namespace PolishOpenData.Bir.Wire;

/// <summary>The BIR operations the library calls. Summary reports (DanePobierzRaportZbiorczy) are deliberately absent: they are deferred.</summary>
internal enum BirOperation
{
    Zaloguj,
    Wyloguj,
    DaneSzukajPodmioty,
    DanePobierzPelnyRaport,
    GetValue,
}

/// <summary>The one parameter a DaneSzukajPodmioty call carries; the member names are the element names on the wire.</summary>
internal enum BirSearchField
{
    Regon,
    Nip,
    Krs,
    Regony9zn,
    Regony14zn,
    Nipy,
    Krsy,
}

/// <summary>Wire names of the operations (GUS BIR1.2 manual: GetValue lives in another namespace than the rest).</summary>
internal static class BirOperations
{
    public static string Name(BirOperation operation) => operation switch
    {
        BirOperation.Zaloguj => nameof(BirOperation.Zaloguj),
        BirOperation.Wyloguj => nameof(BirOperation.Wyloguj),
        BirOperation.DaneSzukajPodmioty => nameof(BirOperation.DaneSzukajPodmioty),
        BirOperation.DanePobierzPelnyRaport => nameof(BirOperation.DanePobierzPelnyRaport),
        BirOperation.GetValue => nameof(BirOperation.GetValue),
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown BIR operation."),
    };

    /// <summary>The element that holds the answer: the operation name plus <c>Result</c> (<c>ZalogujResult</c>, ...).</summary>
    public static string ResultElement(BirOperation operation) => Name(operation) + "Result";

    /// <summary>The XML namespace of the result element: the BIR namespace for GetValue, the PUBL namespace for everything else.</summary>
    public static string ResultNamespace(BirOperation operation) =>
        operation == BirOperation.GetValue ? BirEnvelope.Bir : BirEnvelope.Publ;
}

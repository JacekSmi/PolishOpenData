using System;

namespace PolishOpenData.Bir.Wire;

/// <summary>One BIR call, already serialised: the SOAP envelope plus the values the HTTP request needs besides it.</summary>
internal sealed class BirRequest
{
    private BirRequest(BirOperation operation, Uri endpoint, byte[] body, string action)
    {
        Operation = operation;
        Endpoint = endpoint;
        Body = body;
        Action = action;
    }

    public BirOperation Operation { get; }

    /// <summary>The service address: the HTTP URL and, inside the envelope, <c>wsa:To</c>.</summary>
    public Uri Endpoint { get; }

    /// <summary>The UTF-8 SOAP 1.2 envelope, without a byte order mark.</summary>
    public byte[] Body { get; }

    /// <summary>The <c>wsa:Action</c>; also sent as the <c>action="..."</c> parameter of the request Content-Type.</summary>
    public string Action { get; }

    public static BirRequest Login(Uri endpoint, string userKey)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(userKey);
        var body = BirEnvelope.Zaloguj(endpoint, userKey, out var action);
        return new BirRequest(BirOperation.Zaloguj, endpoint, body, action);
    }

    public static BirRequest Logout(Uri endpoint, string sid)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(sid);
        var body = BirEnvelope.Wyloguj(endpoint, sid, out var action);
        return new BirRequest(BirOperation.Wyloguj, endpoint, body, action);
    }

    public static BirRequest Search(Uri endpoint, BirSearchField field, string value)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(value);
        var body = BirEnvelope.Search(endpoint, ElementName(field), value, out var action);
        return new BirRequest(BirOperation.DaneSzukajPodmioty, endpoint, body, action);
    }

    public static BirRequest Report(Uri endpoint, string regon, string reportWireName)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(regon);
        ArgumentNullException.ThrowIfNull(reportWireName);
        var body = BirEnvelope.FullReport(endpoint, regon, reportWireName, out var action);
        return new BirRequest(BirOperation.DanePobierzPelnyRaport, endpoint, body, action);
    }

    public static BirRequest GetValue(Uri endpoint, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(parameterName);
        var body = BirEnvelope.GetValue(endpoint, parameterName, out var action);
        return new BirRequest(BirOperation.GetValue, endpoint, body, action);
    }

    // nameof, not Enum.ToString(): the element names are wire names and must survive trimming and renames of the enum members' text.
    private static string ElementName(BirSearchField field) => field switch
    {
        BirSearchField.Regon => nameof(BirSearchField.Regon),
        BirSearchField.Nip => nameof(BirSearchField.Nip),
        BirSearchField.Krs => nameof(BirSearchField.Krs),
        BirSearchField.Regony9zn => nameof(BirSearchField.Regony9zn),
        BirSearchField.Regony14zn => nameof(BirSearchField.Regony14zn),
        BirSearchField.Nipy => nameof(BirSearchField.Nipy),
        BirSearchField.Krsy => nameof(BirSearchField.Krsy),
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown search field."),
    };
}

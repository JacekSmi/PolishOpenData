using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Xml.Linq;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Bir.Tests;

/// <summary>One request the fake BIR service received, parsed from its SOAP envelope and HTTP headers.</summary>
internal sealed class FakeBirCall
{
    public FakeBirCall(string operation, string? sid, string? action, string? wsaTo, string contentType, string body, IReadOnlyDictionary<string, string> arguments)
    {
        Operation = operation;
        Sid = sid;
        Action = action;
        WsaTo = wsaTo;
        ContentType = contentType;
        Body = body;
        Arguments = arguments;
    }

    /// <summary>The local name of the child of the SOAP body, for example <c>DaneSzukajPodmioty</c>.</summary>
    public string Operation { get; }

    /// <summary>The value of the <c>sid</c> request header.</summary>
    public string? Sid { get; }

    /// <summary>The <c>action="..."</c> parameter of the Content-Type, without quotes.</summary>
    public string? Action { get; }

    /// <summary>The text of <c>wsa:To</c>.</summary>
    public string? WsaTo { get; }

    public string ContentType { get; }

    public string Body { get; }

    /// <summary>The text of every leaf element of the operation element, by local name (<c>pRegon</c>, <c>Nip</c>, ...).</summary>
    public IReadOnlyDictionary<string, string> Arguments { get; }

    /// <summary>One argument, or null when the request did not carry it.</summary>
    public string? Arg(string name) => Arguments.TryGetValue(name, out var value) ? value : null;
}

/// <summary>
/// A fake of the whole BIR service behind a <see cref="StubHttpMessageHandler"/>: it parses each request, keeps sessions,
/// answers routed searches and reports from fixtures and fails loudly on a request nobody routed. It never touches the network.
/// </summary>
internal sealed class FakeBirServer
{
    /// <summary>The first sid issued; the n-th login returns <c>sidsidsidsid</c> followed by n as 8 digits.</summary>
    public const string FirstSid = "sidsidsidsid00000001";

    private static readonly string[] SearchFields = ["Regon", "Nip", "Krs", "Regony9zn", "Regony14zn", "Nipy", "Krsy"];

    private readonly object _gate = new();
    private readonly List<FakeBirCall> _calls = new();
    private readonly Dictionary<string, Func<HttpResponseMessage>> _routes = new(StringComparer.Ordinal);
    private readonly List<Func<FakeBirCall, HttpResponseMessage?>> _custom = new();
    private readonly HashSet<string> _validSids = new(StringComparer.Ordinal);
    private int _logins;
    private bool _rejectKey;

    public FakeBirServer() => Handler = new StubHttpMessageHandler(Handle);

    public StubHttpMessageHandler Handler { get; }

    /// <summary>A snapshot of the calls so far, in arrival order.</summary>
    public IReadOnlyList<FakeBirCall> Calls
    {
        get
        {
            lock (_gate)
            {
                return _calls.ToArray();
            }
        }
    }

    public int Count(string operation)
    {
        lock (_gate)
        {
            return _calls.Count(c => string.Equals(c.Operation, operation, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Routes the search with exactly this field and value to a fixture: <paramref name="fixture"/> is a name under
    /// tests/Fixtures/regon without extension; <c>name.mtom</c> (a recorded answer) is served as stored, else <c>name.xml</c> (an inner result) is wrapped.
    /// </summary>
    public FakeBirServer Search(string field, string value, string fixture) => AddRoute(SearchKey(field, value), "DaneSzukajPodmioty", fixture);

    /// <summary>Answers the search with the in-band ErrorCode 4 the service sends for "not found", echoing the value.</summary>
    public FakeBirServer SearchNotFound(string field, string value)
    {
        var inner = "<root>\r\n  <dane>\r\n    <ErrorCode>4</ErrorCode>\r\n"
            + "    <ErrorMessagePl>Nie znaleziono podmiotu dla podanych kryteriów wyszukiwania.</ErrorMessagePl>\r\n"
            + "    <ErrorMessageEn>No data found for the specified search criteria.</ErrorMessageEn>\r\n"
            + "    <" + field + ">" + new XText(value) + "</" + field + ">\r\n  </dane>\r\n</root>";
        return AddRoute(SearchKey(field, value), () => Ok("DaneSzukajPodmioty", inner));
    }

    /// <summary>Routes the full report with exactly this REGON and report wire name to a fixture (see <see cref="Search"/>).</summary>
    public FakeBirServer Report(string regon, string reportWireName, string fixture) => AddRoute(ReportKey(regon, reportWireName), "DanePobierzPelnyRaport", fixture);

    /// <summary>Makes <c>GetValue("StanDanych")</c> answer <paramref name="text"/> and <c>GetValue("KomunikatKod")</c> answer 0.</summary>
    public FakeBirServer StanDanych(string text)
    {
        AddRoute(GetValueKey("StanDanych"), () => Ok("GetValue", text));
        return AddRoute(GetValueKey("KomunikatKod"), () => Ok("GetValue", "0"));
    }

    /// <summary>Makes every login answer an empty result, as the service does for a key it does not know.</summary>
    public FakeBirServer RejectKey()
    {
        lock (_gate)
        {
            _rejectKey = true;
        }

        return this;
    }

    /// <summary>Invalidates every sid issued so far: data calls and GetValue with it get an empty result until a new login.</summary>
    public FakeBirServer ExpireSessions()
    {
        lock (_gate)
        {
            _validSids.Clear();
        }

        return this;
    }

    /// <summary>Adds a handler that is asked before everything else; returning null lets the call fall through to the routes.</summary>
    public FakeBirServer Respond(Func<FakeBirCall, HttpResponseMessage?> custom)
    {
        lock (_gate)
        {
            _custom.Add(custom);
        }

        return this;
    }

    private static string SearchKey(string field, string value) => "search|" + field + "|" + value;

    private static string ReportKey(string regon, string report) => "report|" + regon + "|" + report;

    private static string GetValueKey(string name) => "getvalue|" + name;

    private static HttpResponseMessage Ok(string operation, string? result) =>
        RegonTestResponses.Response(HttpStatusCode.OK, RegonTestResponses.Mtom(operation, result));

    private FakeBirServer AddRoute(string key, string operation, string fixture)
    {
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "Fixtures", "regon", fixture + ".mtom")))
        {
            return AddRoute(key, () => StubHttpMessageHandler.Mtom(fixture));
        }

        var inner = RegonTestResponses.Inner(fixture);   // a missing fixture fails here, while the test is being set up
        return AddRoute(key, () => Ok(operation, inner));
    }

    private FakeBirServer AddRoute(string key, Func<HttpResponseMessage> response)
    {
        lock (_gate)
        {
            _routes[key] = response;
        }

        return this;
    }

    private HttpResponseMessage Handle(HttpRequestMessage request)
    {
        var call = Parse(request);
        Func<FakeBirCall, HttpResponseMessage?>[] custom;
        lock (_gate)
        {
            _calls.Add(call);
            custom = _custom.ToArray();
        }

        foreach (var handler in custom)
        {
            var response = handler(call);
            if (response is not null)
            {
                return response;
            }
        }

        switch (call.Operation)
        {
            case "Zaloguj":
                return Login();
            case "Wyloguj":
                return Logout(call);
            case "DaneSzukajPodmioty":
            case "DanePobierzPelnyRaport":
            case "GetValue":
                return Data(call);
            default:
                throw NoRoute(call);
        }
    }

    private HttpResponseMessage Login()
    {
        string? sid = null;
        lock (_gate)
        {
            if (!_rejectKey)
            {
                _logins++;
                sid = "sidsidsidsid" + _logins.ToString("D8", CultureInfo.InvariantCulture);
                _validSids.Add(sid);
            }
        }

        return Ok("Zaloguj", sid);
    }

    private HttpResponseMessage Logout(FakeBirCall call)
    {
        var sid = call.Arg("pIdentyfikatorSesji");
        lock (_gate)
        {
            if (sid is not null)
            {
                _validSids.Remove(sid);
            }
        }

        return Ok("Wyloguj", "true");
    }

    private HttpResponseMessage Data(FakeBirCall call)
    {
        bool valid;
        lock (_gate)
        {
            valid = call.Sid is not null && _validSids.Contains(call.Sid);
        }

        if (!valid)
        {
            return Ok(call.Operation, null);   // a dead session: HTTP 200 and an empty result (dotnet-approach.md section 3.6)
        }

        string key;
        if (string.Equals(call.Operation, "DaneSzukajPodmioty", StringComparison.Ordinal))
        {
            var field = SearchFields.FirstOrDefault(call.Arguments.ContainsKey);
            key = field is null ? "search|" : SearchKey(field, call.Arguments[field]);
        }
        else if (string.Equals(call.Operation, "DanePobierzPelnyRaport", StringComparison.Ordinal))
        {
            key = ReportKey(call.Arg("pRegon") ?? string.Empty, call.Arg("pNazwaRaportu") ?? string.Empty);
        }
        else
        {
            key = GetValueKey(call.Arg("pNazwaParametru") ?? string.Empty);
        }

        Func<HttpResponseMessage>? route;
        lock (_gate)
        {
            _routes.TryGetValue(key, out route);
        }

        return route is null ? throw NoRoute(call) : route();
    }

    private static InvalidOperationException NoRoute(FakeBirCall call) =>
        new("FakeBirServer: no route for " + call.Operation + " " + string.Join(", ", call.Arguments.Select(static a => a.Key + "=" + a.Value)));

    private static FakeBirCall Parse(HttpRequestMessage request)
    {
        // The stub has already buffered the content before it calls the responder, so this read does not block on I/O.
        var body = request.Content is null ? string.Empty : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        XNamespace soap = "http://www.w3.org/2003/05/soap-envelope";
        XNamespace wsa = "http://www.w3.org/2005/08/addressing";
        var envelope = XDocument.Parse(body).Root!;
        var operation = envelope.Element(soap + "Body")!.Elements().First();
        var arguments = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var leaf in operation.Descendants().Where(static e => !e.HasElements))
        {
            arguments[leaf.Name.LocalName] = leaf.Value;
        }

        var contentType = request.Content?.Headers.ContentType;
        var action = contentType?.Parameters.FirstOrDefault(static p => string.Equals(p.Name, "action", StringComparison.OrdinalIgnoreCase))?.Value;
        return new FakeBirCall(
            operation.Name.LocalName,
            request.Headers.TryGetValues("sid", out var sids) ? string.Join(" ", sids) : null,
            action?.Trim('"'),
            envelope.Element(soap + "Header")?.Element(wsa + "To")?.Value,
            contentType?.ToString() ?? string.Empty,
            body,
            arguments);
    }
}

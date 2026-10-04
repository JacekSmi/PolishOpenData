using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using PolishOpenData.Internal;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Bir.Tests;

public class FakeBirServerTests
{
    private const string Endpoint = "https://fake.test/wsBIR/UslugaBIRzewnPubl.svc";
    private const string PublNamespace = "http://CIS/BIR/PUBL/2014/07";
    private const string BirNamespace = "http://CIS/BIR/2014/07";
    private const string PublAction = PublNamespace + "/IUslugaBIRzewnPubl/";
    private const string BirAction = BirNamespace + "/IUslugaBIR/";
    private const string DummyKey = "dummy-key-not-a-secret";

    // Requests are written by hand here on purpose: the fakes are tested without the library code.
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string ns, string action, string operation, string bodyXml, string? sid)
    {
        var envelope =
            "<soap:Envelope xmlns:soap=\"http://www.w3.org/2003/05/soap-envelope\" xmlns:ns=\"" + ns + "\" xmlns:dat=\"" + PublNamespace + "/DataContract\">"
            + "<soap:Header xmlns:wsa=\"http://www.w3.org/2005/08/addressing\"><wsa:To>" + Endpoint + "</wsa:To><wsa:Action>" + action + "</wsa:Action></soap:Header>"
            + "<soap:Body><ns:" + operation + ">" + bodyXml + "</ns:" + operation + "></soap:Body></soap:Envelope>";
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(envelope));
        var mediaType = new MediaTypeHeaderValue("application/soap+xml") { CharSet = "utf-8" };
        mediaType.Parameters.Add(new NameValueHeaderValue("action", "\"" + action + "\""));
        content.Headers.ContentType = mediaType;
        request.Content = content;
        if (sid is not null)
        {
            request.Headers.TryAddWithoutValidation("sid", sid);
        }

        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string key = DummyKey) =>
        PostAsync(client, PublNamespace, PublAction + "Zaloguj", "Zaloguj", "<ns:pKluczUzytkownika>" + key + "</ns:pKluczUzytkownika>", null);

    private static Task<HttpResponseMessage> LogoutAsync(HttpClient client, string sid) =>
        PostAsync(client, PublNamespace, PublAction + "Wyloguj", "Wyloguj", "<ns:pIdentyfikatorSesji>" + sid + "</ns:pIdentyfikatorSesji>", sid);

    private static Task<HttpResponseMessage> SearchAsync(HttpClient client, string sid, string field, string value) =>
        PostAsync(
            client,
            PublNamespace,
            PublAction + "DaneSzukajPodmioty",
            "DaneSzukajPodmioty",
            "<ns:pParametryWyszukiwania><dat:" + field + ">" + value + "</dat:" + field + "></ns:pParametryWyszukiwania>",
            sid);

    private static Task<HttpResponseMessage> ReportAsync(HttpClient client, string sid, string regon, string report) =>
        PostAsync(
            client,
            PublNamespace,
            PublAction + "DanePobierzPelnyRaport",
            "DanePobierzPelnyRaport",
            "<ns:pRegon>" + regon + "</ns:pRegon><ns:pNazwaRaportu>" + report + "</ns:pNazwaRaportu>",
            sid);

    private static Task<HttpResponseMessage> GetValueAsync(HttpClient client, string sid, string name) =>
        PostAsync(client, BirNamespace, BirAction + "GetValue", "GetValue", "<ns:pNazwaParametru>" + name + "</ns:pNazwaParametru>", sid);

    // A response body can be read once only (on .NET Framework the helper closes the stream), so read it once and pass the text on.
    private static async Task<string> TextAsync(HttpResponseMessage response) =>
        Encoding.UTF8.GetString(await response.Content.ReadBytesAsync(TestContext.Current.CancellationToken));

    // The text of the *Result element of an MTOM answer (the envelope is the only XML in the body).
    private static string ResultOf(string mtomText)
    {
        var start = mtomText.IndexOf("<s:Envelope", StringComparison.Ordinal);
        var end = mtomText.IndexOf("</s:Envelope>", StringComparison.Ordinal) + "</s:Envelope>".Length;
        var envelope = XDocument.Parse(mtomText.Substring(start, end - start));
        return envelope.Descendants().First(static e => e.Name.LocalName.EndsWith("Result", StringComparison.Ordinal)).Value;
    }

    private static async Task<string> ResultAsync(HttpResponseMessage response) => ResultOf(await TextAsync(response));

    private static string RawContentType(HttpResponseMessage response) =>
        response.Content.Headers.TryGetValues("Content-Type", out var values) ? string.Join(", ", values) : string.Empty;

    [Fact]
    public async Task Login_issues_20_character_sids_in_sequence()
    {
        var server = new FakeBirServer();
        using var client = new HttpClient(server.Handler);

        using var first = await LoginAsync(client);
        using var second = await LoginAsync(client);
        using var third = await LoginAsync(client);

        Assert.Equal(FakeBirServer.FirstSid, await ResultAsync(first));
        Assert.Equal("sidsidsidsid00000002", await ResultAsync(second));
        Assert.Equal("sidsidsidsid00000003", await ResultAsync(third));
        Assert.Equal(20, FakeBirServer.FirstSid.Length);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
    }

    [Fact]
    public async Task Search_route_returns_the_fixture()
    {
        var server = new FakeBirServer()
            .Search("Nipy", "5261040828,1234563218", "synthetic-search-batch-nips")
            .SearchNotFound("Nip", "0000000000")
            .Report("000331501", "BIR12OsPrawna", "synthetic-report-osprawna-gus")
            .StanDanych("19-10-2018");
        using var client = new HttpClient(server.Handler);
        using var login = await LoginAsync(client);
        var sid = await ResultAsync(login);

        using var batch = await SearchAsync(client, sid, "Nipy", "5261040828,1234563218");
        Assert.Equal(HttpStatusCode.OK, batch.StatusCode);
        Assert.Equal(RegonTestResponses.ContentType.Replace("; ", ";"), RawContentType(batch).Replace("; ", ";"));
        Assert.Equal(RegonTestResponses.Inner("synthetic-search-batch-nips"), await ResultAsync(batch));

        using var notFound = await SearchAsync(client, sid, "Nip", "0000000000");
        var notFoundInner = await ResultAsync(notFound);
        Assert.Contains("<ErrorCode>4</ErrorCode>", notFoundInner, StringComparison.Ordinal);
        Assert.Contains("<Nip>0000000000</Nip>", notFoundInner, StringComparison.Ordinal);

        using var report = await ReportAsync(client, sid, "000331501", "BIR12OsPrawna");
        Assert.Equal(RegonTestResponses.Inner("synthetic-report-osprawna-gus"), await ResultAsync(report));

        using var dataDate = await GetValueAsync(client, sid, "StanDanych");
        Assert.Equal("19-10-2018", await ResultAsync(dataDate));
        using var code = await GetValueAsync(client, sid, "KomunikatKod");
        Assert.Equal("0", await ResultAsync(code));
    }

    [Fact]
    public async Task Recorded_fixture_is_served_as_stored()
    {
        var server = new FakeBirServer().Search("Regon", "000331501", "search-regon-gus");
        using var client = new HttpClient(server.Handler);
        using var login = await LoginAsync(client);
        var sid = await ResultAsync(login);

        using var response = await SearchAsync(client, sid, "Regon", "000331501");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Fixture.ReadBytes("regon/search-regon-gus.mtom"), await response.Content.ReadBytesAsync(TestContext.Current.CancellationToken));
        var storedContentType = Fixture.Read("regon/search-regon-gus.headers").TrimEnd('\n').Split('\n')[1].Substring("Content-Type: ".Length);
        Assert.Equal(storedContentType.Replace("; ", ";"), RawContentType(response).Replace("; ", ";"));
    }

    [Fact]
    public async Task Unknown_route_throws()
    {
        var server = new FakeBirServer().Search("Nip", "5261040828", "synthetic-search-batch-nips");
        using var client = new HttpClient(server.Handler);
        using var login = await LoginAsync(client);
        var sid = await ResultAsync(login);

        var search = await Assert.ThrowsAsync<InvalidOperationException>(() => SearchAsync(client, sid, "Nip", "1234563218"));
        Assert.StartsWith("FakeBirServer: no route for DaneSzukajPodmioty", search.Message, StringComparison.Ordinal);
        Assert.Contains("Nip=1234563218", search.Message, StringComparison.Ordinal);

        var report = await Assert.ThrowsAsync<InvalidOperationException>(() => ReportAsync(client, sid, "000331501", "BIR12OsPrawna"));
        Assert.Contains("pRegon=000331501", report.Message, StringComparison.Ordinal);
        Assert.Contains("pNazwaRaportu=BIR12OsPrawna", report.Message, StringComparison.Ordinal);

        await Assert.ThrowsAsync<InvalidOperationException>(() => GetValueAsync(client, sid, "StanDanych"));
    }

    [Fact]
    public async Task Expired_sessions_get_empty_results_until_a_new_login()
    {
        var server = new FakeBirServer().Search("Nip", "5261040828", "synthetic-search-batch-nips").StanDanych("19-10-2018");
        using var client = new HttpClient(server.Handler);
        using var login = await LoginAsync(client);
        var oldSid = await ResultAsync(login);
        using var alive = await SearchAsync(client, oldSid, "Nip", "5261040828");
        Assert.NotEmpty(await ResultAsync(alive));

        server.ExpireSessions();

        using var dead = await SearchAsync(client, oldSid, "Nip", "5261040828");
        var deadText = await TextAsync(dead);
        Assert.Contains("<DaneSzukajPodmiotyResult/>", deadText, StringComparison.Ordinal);
        Assert.Equal(string.Empty, ResultOf(deadText));
        using var deadGetValue = await GetValueAsync(client, oldSid, "StanDanych");
        Assert.Equal(string.Empty, await ResultAsync(deadGetValue));

        using var relogin = await LoginAsync(client);
        var newSid = await ResultAsync(relogin);
        Assert.NotEqual(oldSid, newSid);
        using var revived = await SearchAsync(client, newSid, "Nip", "5261040828");
        Assert.NotEmpty(await ResultAsync(revived));
        using var stillDead = await SearchAsync(client, oldSid, "Nip", "5261040828");
        Assert.Equal(string.Empty, await ResultAsync(stillDead));
    }

    [Fact]
    public async Task Logout_invalidates_the_sid_and_answers_true()
    {
        var server = new FakeBirServer().Search("Nip", "5261040828", "synthetic-search-batch-nips");
        using var client = new HttpClient(server.Handler);
        using var login = await LoginAsync(client);
        var sid = await ResultAsync(login);

        using var logout = await LogoutAsync(client, sid);
        Assert.Equal("true", await ResultAsync(logout));

        using var afterwards = await SearchAsync(client, sid, "Nip", "5261040828");
        Assert.Equal(string.Empty, await ResultAsync(afterwards));
    }

    [Fact]
    public async Task RejectKey_answers_an_empty_login()
    {
        var server = new FakeBirServer().RejectKey();
        using var client = new HttpClient(server.Handler);

        using var login = await LoginAsync(client, "dummy-rejected-key");

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(string.Empty, await ResultAsync(login));
        Assert.Equal(1, server.Count("Zaloguj"));
    }

    [Fact]
    public async Task Respond_is_checked_first_and_null_falls_through()
    {
        var server = new FakeBirServer().Search("Nip", "5261040828", "synthetic-search-batch-nips");
        server.Respond(call => string.Equals(call.Arg("pKluczUzytkownika"), "dummy-bad-key", StringComparison.Ordinal)
            ? StubHttpMessageHandler.Html(HttpStatusCode.UnsupportedMediaType, "<html>415</html>")
            : null);
        using var client = new HttpClient(server.Handler);

        using var custom = await LoginAsync(client, "dummy-bad-key");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, custom.StatusCode);

        using var fallThrough = await LoginAsync(client);
        Assert.Equal(HttpStatusCode.OK, fallThrough.StatusCode);
        Assert.Equal(FakeBirServer.FirstSid, await ResultAsync(fallThrough));
        Assert.Equal(2, server.Count("Zaloguj"));
    }

    [Fact]
    public async Task Counts_calls_per_operation()
    {
        var server = new FakeBirServer().Search("Nip", "5261040828", "synthetic-search-batch-nips");
        using var client = new HttpClient(server.Handler);
        using var first = await LoginAsync(client);
        using var second = await LoginAsync(client);
        var sid = await ResultAsync(first);
        for (var i = 0; i < 3; i++)
        {
            using var search = await SearchAsync(client, sid, "Nip", "5261040828");
        }

        using var logout = await LogoutAsync(client, sid);

        Assert.Equal(2, server.Count("Zaloguj"));
        Assert.Equal(3, server.Count("DaneSzukajPodmioty"));
        Assert.Equal(1, server.Count("Wyloguj"));
        Assert.Equal(0, server.Count("GetValue"));
        Assert.Equal(6, server.Calls.Count);
        Assert.Equal(
            new[] { "Zaloguj", "Zaloguj", "DaneSzukajPodmioty", "DaneSzukajPodmioty", "DaneSzukajPodmioty", "Wyloguj" },
            server.Calls.Select(static c => c.Operation).ToArray());
    }

    [Fact]
    public async Task Calls_expose_sid_action_and_arguments()
    {
        var server = new FakeBirServer().Search("Regon", "000331501", "search-regon-gus").StanDanych("19-10-2018");
        using var client = new HttpClient(server.Handler);
        using var login = await LoginAsync(client);
        var sid = await ResultAsync(login);
        using var search = await SearchAsync(client, sid, "Regon", "000331501");

        var loginCall = server.Calls[0];
        Assert.Equal("Zaloguj", loginCall.Operation);
        Assert.Null(loginCall.Sid);
        Assert.Equal(PublAction + "Zaloguj", loginCall.Action);
        Assert.Equal(DummyKey, loginCall.Arg("pKluczUzytkownika"));

        var searchCall = server.Calls[1];
        Assert.Equal("DaneSzukajPodmioty", searchCall.Operation);
        Assert.Equal(sid, searchCall.Sid);
        Assert.Equal(PublAction + "DaneSzukajPodmioty", searchCall.Action);
        Assert.Equal(Endpoint, searchCall.WsaTo);
        Assert.Equal("application/soap+xml; charset=utf-8; action=\"" + PublAction + "DaneSzukajPodmioty\"", searchCall.ContentType);
        Assert.Contains("<dat:Regon>000331501</dat:Regon>", searchCall.Body, StringComparison.Ordinal);
        Assert.Equal("000331501", searchCall.Arg("Regon"));
        Assert.Equal("000331501", searchCall.Arguments["Regon"]);
        Assert.Null(searchCall.Arg("Nip"));
        Assert.False(searchCall.Arguments.ContainsKey("pParametryWyszukiwania"));   // only leaf elements are arguments

        using var getValue = await GetValueAsync(client, sid, "KomunikatKod");
        var getValueCall = server.Calls[2];
        Assert.Equal("GetValue", getValueCall.Operation);
        Assert.Equal(BirAction + "GetValue", getValueCall.Action);
        Assert.Equal("KomunikatKod", getValueCall.Arg("pNazwaParametru"));
    }
}

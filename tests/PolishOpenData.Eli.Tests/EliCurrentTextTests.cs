using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Eli.Tests;

public class EliCurrentTextTests
{
    private static readonly EliActId KcBase = EliActId.Parse("DU/1964/93");

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly TimeProvider Today = new FixedTime(new DateTimeOffset(2026, 11, 14, 9, 0, 0, TimeSpan.Zero));

    private static EliClient Client(StubHttpMessageHandler handler, TimeProvider? time = null) => new(new HttpClient(handler), null, time ?? Today);

    private static string ActJson(string id, bool html, string? announced, string? refs = null, string? inForce = null, string? status = null)
    {
        var parts = id.Split('/');
        return "{\"publisher\":\"" + parts[0] + "\",\"year\":" + parts[1] + ",\"pos\":" + parts[2] + ",\"displayAddress\":\"Dz.U. " + parts[1] + " poz. " + parts[2] + "\",\"title\":\"t\","
            + "\"textHTML\":" + (html ? "true" : "false") + ",\"textPDF\":true"
            + (announced is null ? string.Empty : ",\"announcementDate\":\"" + announced + "\"")
            + (inForce is null ? string.Empty : ",\"inForce\":\"" + inForce + "\"")
            + (status is null ? string.Empty : ",\"status\":\"" + status + "\"")
            + (refs is null ? string.Empty : ",\"references\":{" + refs + "}") + "}";
    }

    private static string Consolidated(params string[] ids) =>
        "\"Inf. o tekście jednolitym\":[" + string.Join(",", ids.Select(i => "{\"id\":\"" + i + "\"}")) + "]";

    private static string Amending(params (string Id, string? Date)[] items) =>
        "\"Akty zmieniające\":[" + string.Join(",", items.Select(i => "{\"id\":\"" + i.Id + "\"" + (i.Date is null ? string.Empty : ",\"date\":\"" + i.Date + "\"") + "}")) + "]";

    private static StubHttpMessageHandler Router(Func<string, HttpResponseMessage?> custom) => new(r =>
    {
        var path = r.RequestUri!.AbsolutePath;
        if (custom(path) is { } response)
        {
            return response;
        }

        return path switch
        {
            "/eli/acts/DU/1964/93" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "eli/act-DU-1964-93.json"),
            "/eli/acts/DU/2026/795" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "eli/act-DU-2026-795.json"),
            "/eli/acts/DU/2025/1071" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "eli/act-DU-2025-1071.json"),
            "/eli/acts/DU/2024/1061" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "eli/act-DU-2024-1061.json"),
            _ => StubHttpMessageHandler.Empty(HttpStatusCode.NotFound),
        };
    });

    private static StubHttpMessageHandler KcRouter() => Router(_ => null);

    private static HttpResponseMessage Json(string body) => StubHttpMessageHandler.Json(HttpStatusCode.OK, body);

    [Fact]
    public async Task KC_NewestIsPdfOnly_NewestWithHtmlIs2024()
    {
        var handler = KcRouter();

        var current = await Client(handler).GetCurrentTextAsync(KcBase, TestContext.Current.CancellationToken);

        Assert.Equal("DU/2026/795", current.NewestConsolidated!.Id.ToString());
        Assert.False(current.NewestConsolidated.HasHtml);
        Assert.Equal("DU/2024/1061", current.NewestWithHtml!.Id.ToString());
        Assert.Equal(3, current.ConsolidatedTextsChecked);
        Assert.Equal(4, handler.RequestUris.Count);   // the base act and three consolidated texts
        Assert.False(current.IsHtmlTextCurrent);
    }

    [Fact]
    public async Task KC_AmendmentsSplitIntoInForceSinceAndFuture()
    {
        var current = await Client(KcRouter()).GetCurrentTextAsync(KcBase, TestContext.Current.CancellationToken);

        Assert.Equal(
            ["DU/2024/1237", "DU/2025/769", "DU/2025/1172", "DU/2025/1508", "DU/2026/184"],
            current.InForceSince.Select(a => a.Id.ToString()).OrderBy(i => i, new IdComparer()).ToArray());
        Assert.Equal("DU/2026/507", Assert.Single(current.FutureSince).Id.ToString());
        Assert.Equal(new DateOnly(2028, 11, 1), current.FutureSince[0].EffectiveDate);
    }

    private sealed class IdComparer : System.Collections.Generic.IComparer<string>
    {
        public int Compare(string? x, string? y) => Key(x!).CompareTo(Key(y!));

        private static (int, int) Key(string id) => (int.Parse(id.Split('/')[1], System.Globalization.CultureInfo.InvariantCulture), int.Parse(id.Split('/')[2], System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task NoConsolidatedTexts_NoAmendments_BaseHtmlIsCurrent()
    {
        var handler = Router(p => p == "/eli/acts/DU/2020/7" ? Json(ActJson("DU/2020/7", html: true, "2020-01-02")) : null);

        var current = await Client(handler).GetCurrentTextAsync(EliActId.Parse("DU/2020/7"), TestContext.Current.CancellationToken);

        Assert.Equal("DU/2020/7", current.NewestWithHtml!.Id.ToString());
        Assert.Null(current.NewestConsolidated);
        Assert.True(current.IsHtmlTextCurrent);
        Assert.Equal(0, current.ConsolidatedTextsChecked);
        Assert.Single(handler.RequestUris);
    }

    [Fact]
    public async Task BaseHtmlWithAmendments_IsNotCurrent()
    {
        var handler = Router(p => p == "/eli/acts/DU/2020/7"
            ? Json(ActJson("DU/2020/7", html: true, "2020-01-02", Amending(("DU/2025/5", "2025-03-01"))))
            : null);

        var current = await Client(handler).GetCurrentTextAsync(EliActId.Parse("DU/2020/7"), TestContext.Current.CancellationToken);

        Assert.Equal("DU/2020/7", current.NewestWithHtml!.Id.ToString());
        Assert.Equal("DU/2025/5", Assert.Single(current.InForceSince).Id.ToString());
        Assert.False(current.IsHtmlTextCurrent);
    }

    [Fact]
    public async Task OriginalTextOfABaseActWithConsolidatedTexts_IsNeverQuotedAsTheNewestWithHtml()
    {
        // the base act has HTML (its original text) but all its consolidated texts are PDF-only
        var handler = Router(p => p switch
        {
            "/eli/acts/DU/2000/1" => Json(ActJson("DU/2000/1", html: true, "2000-01-02", Consolidated("DU/2026/9", "DU/2025/9"))),
            "/eli/acts/DU/2026/9" => Json(ActJson("DU/2026/9", html: false, "2026-02-01")),
            "/eli/acts/DU/2025/9" => Json(ActJson("DU/2025/9", html: false, "2025-02-01")),
            _ => null,
        });

        var current = await Client(handler).GetCurrentTextAsync(EliActId.Parse("DU/2000/1"), TestContext.Current.CancellationToken);

        Assert.Null(current.NewestWithHtml);
        Assert.Equal("DU/2026/9", current.NewestConsolidated!.Id.ToString());
        Assert.False(current.IsHtmlTextCurrent);
    }

    [Fact]
    public async Task OnlyTheFiveNewestConsolidatedTextsAreLookedAt_AtMostSixRequests()
    {
        var ids = Enumerable.Range(1, 8).Select(n => "DU/2026/" + (100 - n)).ToArray();
        var handler = Router(p =>
        {
            if (p == "/eli/acts/DU/2000/1")
            {
                return Json(ActJson("DU/2000/1", html: true, "2000-01-02", Consolidated(ids)));
            }

            return p.StartsWith("/eli/acts/DU/2026/", StringComparison.Ordinal) ? Json(ActJson(p.Substring("/eli/acts/".Length), html: false, "2026-02-01")) : null;
        });

        var current = await Client(handler).GetCurrentTextAsync(EliActId.Parse("DU/2000/1"), TestContext.Current.CancellationToken);

        Assert.Equal(5, current.ConsolidatedTextsChecked);
        Assert.Equal(6, handler.RequestUris.Count);
        Assert.Null(current.NewestWithHtml);
    }

    [Fact]
    public async Task ConsolidatedTexts_AreOrderedByIdNotByTheListOrder()
    {
        var handler = Router(p => p switch
        {
            "/eli/acts/DU/2000/1" => Json(ActJson("DU/2000/1", html: true, "2000-01-02", Consolidated("DU/2024/1061", "DU/2026/795"))),
            _ => null,
        });

        var current = await Client(handler).GetCurrentTextAsync(EliActId.Parse("DU/2000/1"), TestContext.Current.CancellationToken);

        Assert.Equal("DU/2026/795", current.NewestConsolidated!.Id.ToString());
        Assert.Equal("DU/2024/1061", current.NewestWithHtml!.Id.ToString());
    }

    [Fact]
    public async Task ANewestConsolidatedTextThatCannotBeFound_MeansCurrencyCannotBeConfirmed()
    {
        var handler = Router(p => p switch
        {
            "/eli/acts/DU/2000/1" => Json(ActJson("DU/2000/1", html: true, "2000-01-02", Consolidated("DU/2026/1", "DU/2024/1061"))),
            _ => null,   // DU/2026/1 falls through to 404
        });

        var current = await Client(handler).GetCurrentTextAsync(EliActId.Parse("DU/2000/1"), TestContext.Current.CancellationToken);

        Assert.Equal("DU/2024/1061", current.NewestWithHtml!.Id.ToString());
        Assert.Equal(2, current.ConsolidatedTextsChecked);
        Assert.False(current.IsHtmlTextCurrent);
    }

    [Fact]
    public async Task AConsolidatedTextWithAnUnreadableDate_FailsLoudly()
    {
        var handler = Router(p => p switch
        {
            "/eli/acts/DU/2000/1" => Json(ActJson("DU/2000/1", html: true, "2000-01-02", Consolidated("DU/2026/9"))),
            "/eli/acts/DU/2026/9" => Json(ActJson("DU/2026/9", html: false, "02.01.2026")),
            _ => null,
        });

        await Assert.ThrowsAsync<PolishOpenDataApiException>(() => Client(handler).GetCurrentTextAsync(EliActId.Parse("DU/2000/1"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BaseActNotFound_Throws()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Client(KcRouter()).GetCurrentTextAsync(EliActId.Parse("DU/2020/999999"), TestContext.Current.CancellationToken));

        Assert.Contains("DU/2020/999999", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAmendmentWithoutADate_CountsAsChangedSince_AndOneOnTheAnnouncementDayDoesNot()
    {
        var handler = Router(p => p == "/eli/acts/DU/2020/7"
            ? Json(ActJson("DU/2020/7", html: true, "2020-01-02", Amending(("DU/2021/1", null), ("DU/2020/9", "2020-01-02"), ("DU/2020/10", "2020-01-03"))))
            : null);

        var current = await Client(handler).GetCurrentTextAsync(EliActId.Parse("DU/2020/7"), TestContext.Current.CancellationToken);

        Assert.Equal(["DU/2020/10", "DU/2021/1"], current.InForceSince.Select(a => a.Id.ToString()).OrderBy(i => i, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task FromAnActAlreadyRead_DoesNotRequestTheBaseActAgain()
    {
        var handler = KcRouter();
        var client = Client(handler);
        var act = await client.GetActAsync(KcBase, TestContext.Current.CancellationToken);
        handler.RequestUris.Clear();

        var current = await client.GetCurrentTextAsync(act!, TestContext.Current.CancellationToken);

        Assert.Equal("DU/2024/1061", current.NewestWithHtml!.Id.ToString());
        Assert.Equal(3, handler.RequestUris.Count);
        Assert.DoesNotContain(handler.RequestUris, u => u.AbsolutePath == "/eli/acts/DU/1964/93");
    }

    [Fact]
    public async Task EmptyId_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Client(KcRouter()).GetCurrentTextAsync(default(EliActId), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ARepealedAct_IsNeverCurrent_EvenWithHtmlAndNothingElse()
    {
        var handler = Router(p => p == "/eli/acts/DU/2022/9" ? Json(ActJson("DU/2022/9", html: true, "2022-01-02", inForce: "NOT_IN_FORCE", status: "uchylony")) : null);

        var current = await Client(handler).GetCurrentTextAsync(EliActId.Parse("DU/2022/9"), TestContext.Current.CancellationToken);

        Assert.False(current.IsHtmlTextCurrent);
        Assert.False(current.BaseActInForce);
        Assert.Equal("uchylony", current.BaseActStatus);
        Assert.Equal("DU/2022/9", current.NewestWithHtml!.Id.ToString());
    }

    [Fact]
    public async Task AnActInForce_WithNothingElse_StaysCurrent()
    {
        var handler = Router(p => p == "/eli/acts/DU/2022/9" ? Json(ActJson("DU/2022/9", html: true, "2022-01-02", inForce: "IN_FORCE", status: "obowi\u0105zuj\u0105cy")) : null);

        var current = await Client(handler).GetCurrentTextAsync(EliActId.Parse("DU/2022/9"), TestContext.Current.CancellationToken);

        Assert.True(current.IsHtmlTextCurrent);
        Assert.True(current.BaseActInForce);
    }

    [Fact]
    public async Task ANewestConsolidatedTextThatIsNotInForce_IsNotCurrent()
    {
        var handler = Router(p => p switch
        {
            "/eli/acts/DU/2000/1" => Json(ActJson("DU/2000/1", html: true, "2000-01-02", Consolidated("DU/2024/1061"), inForce: "IN_FORCE")),
            _ => null,   // DU/2024/1061 is the fixture: NOT_IN_FORCE (wyga\u015bni\u0119cie aktu)
        });

        var current = await Client(handler).GetCurrentTextAsync(EliActId.Parse("DU/2000/1"), TestContext.Current.CancellationToken);

        Assert.Equal("DU/2024/1061", current.NewestWithHtml!.Id.ToString());
        Assert.False(current.IsHtmlTextCurrent);
    }

    [Fact]
    public async Task AStatusThatPromisesAConsolidatedText_ButNoneListed_IsUnconfirmed()
    {
        var handler = Router(p => p == "/eli/acts/DU/2000/1"
            ? Json(ActJson("DU/2000/1", html: true, "2000-01-02", inForce: "IN_FORCE", status: "akt posiada tekst jednolity"))
            : null);

        var current = await Client(handler).GetCurrentTextAsync(EliActId.Parse("DU/2000/1"), TestContext.Current.CancellationToken);

        Assert.False(current.IsHtmlTextCurrent);
    }

    [Fact]
    public async Task TodayIsTheWarsawDate_NotTheUtcDate()
    {
        // 23:30 UTC on 13 November is 00:30 on 14 November in Warsaw: an amendment effective on the 14th is already in force
        var lateEvening = new FixedTime(new DateTimeOffset(2026, 11, 13, 23, 30, 0, TimeSpan.Zero));
        var handler = Router(p => p == "/eli/acts/DU/2020/7"
            ? Json(ActJson("DU/2020/7", html: true, "2020-01-02", Amending(("DU/2026/20", "2026-11-14"))))
            : null);

        var current = await Client(handler, lateEvening).GetCurrentTextAsync(EliActId.Parse("DU/2020/7"), TestContext.Current.CancellationToken);

        Assert.Equal("DU/2026/20", Assert.Single(current.InForceSince).Id.ToString());
        Assert.Empty(current.FutureSince);
    }
}

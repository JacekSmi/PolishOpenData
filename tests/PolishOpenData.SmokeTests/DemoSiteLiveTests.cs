using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using PuppeteerSharp;
using PuppeteerSharp.Input;

namespace PolishOpenData.SmokeTests;

/// <summary>
/// The deployed demo page (GitHub Pages, <c>POLISHOPENDATA_SMOKE_DEMO_URL</c> overrides it) in the Chrome that <c>CHROME_BIN</c> names:
/// it boots, answers one search, and made exactly one UUG, one ULDK and one RCN request (so none per keystroke). Gated and budgeted by <see cref="Live"/>.
/// </summary>
public sealed class DemoSiteLiveTests
{
    private const string DefaultUrl = "https://jaceksmi.github.io/PolishOpenData/";

    // The prefixes the demo's politeness budget is counted by (host and path, without the scheme).
    private const string UugPrefix = "services.gugik.gov.pl/uug/";
    private const string UldkPrefix = "uldk.gugik.gov.pl/";
    private const string RcnPrefix = "mapy.geoportal.gov.pl/wss/service/rcn";
    private const string TilePrefix = "mapy.geoportal.gov.pl/wss/service/PZGIK/ORTO/";

    [Fact(Timeout = Live.TestTimeout)]
    public async Task The_deployed_page_boots_and_answers_a_search()
    {
        Live.SkipUnlessEnabled();
        var chrome = Environment.GetEnvironmentVariable("CHROME_BIN");
        if (string.IsNullOrWhiteSpace(chrome))
        {
            Assert.Fail("set CHROME_BIN to a Chrome/Chromium executable");
        }

        var url = Environment.GetEnvironmentVariable("POLISHOPENDATA_SMOKE_DEMO_URL");
        if (string.IsNullOrWhiteSpace(url))
        {
            url = DefaultUrl;
        }

        var output = TestContext.Current.TestOutputHelper;
        var requests = new ConcurrentQueue<string>();
        await using var browser = await Puppeteer.LaunchAsync(new LaunchOptions
        {
            ExecutablePath = chrome,
            Headless = true,
            Args = ["--no-sandbox"],
            DefaultViewport = new ViewPortOptions { Width = 1024, Height = 768 },
        });
        await using var page = await browser.NewPageAsync();
        page.Console += (_, e) => output?.WriteLine("[console " + e.Message.Type + "] " + e.Message.Text);
        page.Request += (_, e) => requests.Enqueue(e.Request.Url);
        page.Response += (_, e) =>
        {
            if ((int)e.Response.Status >= 400)
            {
                output?.WriteLine("[http " + (int)e.Response.Status + "] " + e.Response.Url);
            }
        };

        int Count(string prefix) => requests.Count(r => r.Contains(prefix, StringComparison.Ordinal));

        async Task<string> State()
        {
            var text = await page.EvaluateExpressionAsync<string>("document.body.innerText");
            return "URL " + page.Url + "; UUG " + Count(UugPrefix) + ", ULDK " + Count(UldkPrefix) + ", RCN " + Count(RcnPrefix)
                + ", tiles " + Count(TilePrefix) + "; page text: " + (text.Length > 600 ? text[..600] : text);
        }

        async Task WaitFor(string what, string script, int seconds)
        {
            try
            {
                await page.WaitForFunctionAsync(script, new WaitForFunctionOptions { Timeout = seconds * 1000 }).WaitAsync(TestContext.Current.CancellationToken);
            }
            catch (WaitTaskTimeoutException)
            {
                Assert.Fail("Timed out after " + seconds + " s waiting for " + what + ". " + await State());
            }
        }

        var clock = Stopwatch.StartNew();
        await page.GoToAsync(url);
        await WaitFor("#search to be enabled", "() => { const s = document.querySelector('#search'); return !!s && !s.disabled; }", 60);
        output?.WriteLine("Time to ready: " + clock.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + " ms");

        // Typed key by key: the exact request counts asserted below prove that nothing is sent per keystroke.
        await page.TypeAsync("#search", "Warszawa, Krucza 50", new TypeOptions { Delay = 30 });
        await page.Keyboard.PressAsync("Enter");
        await WaitFor("the answer (a median or the smaller-radius message) and the parcel line",
            "() => { const c = document.querySelector('[aria-label=\"Price summary\"]'); return !!c && !!c.querySelector('.parcel') && (!!c.querySelector('.figure') || c.innerText.includes('smaller radius')); }", 45);

        var card = await page.EvaluateExpressionAsync<string>("document.querySelector('[aria-label=\"Price summary\"]').innerText");
        var parcel = await page.EvaluateExpressionAsync<string>("document.querySelector('[aria-label=\"Price summary\"] .parcel')?.innerText ?? ''");
        output?.WriteLine("Card: " + card.ReplaceLineEndings(" | "));
        output?.WriteLine("Requests: UUG " + Count(UugPrefix) + ", ULDK " + Count(UldkPrefix) + ", RCN " + Count(RcnPrefix) + ", orthophoto tiles " + Count(TilePrefix));

        Assert.True(parcel.StartsWith("Parcel 1465", StringComparison.Ordinal), "Parcel line: \"" + parcel + "\". " + await State());
        var figure = await page.EvaluateExpressionAsync<string>("document.querySelector('[aria-label=\"Price summary\"] .figure')?.innerText ?? ''");
        Assert.True(figure.Contains("median", StringComparison.Ordinal) || card.Contains("smaller radius", StringComparison.Ordinal),
            "No median figure and no 'smaller radius' message. " + await State());
        Assert.True(Count(UugPrefix) == 1 && Count(UldkPrefix) == 1 && Count(RcnPrefix) == 1, "Expected exactly 1 UUG, 1 ULDK and 1 RCN request. " + await State());
    }
}

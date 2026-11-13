using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using PolishOpenData.Bdl;

namespace PolishOpenData.SmokeTests;

/// <summary>
/// The BDL client against the live GUS API (gated and budgeted by <see cref="Live"/>; anonymous, no key). Three requests.
/// </summary>
public sealed class BdlLiveTests
{
    private const string Krakow = "011212161011";
    private const int PopulationTotal = 72305;

    [Fact(Timeout = Live.TestTimeout)]
    public async Task Anonymous_finds_a_gmina_and_reads_its_population()
    {
        Live.SkipUnlessEnabled();
        using var http = Live.CreateHttpClient();
        var client = new BdlClient(http);
        var ct = TestContext.Current.CancellationToken;

        try
        {
            var units = await client.SearchUnitsAsync("Kraków", BdlLevel.Gmina, ct);
            var gmina = Assert.Single(units, u => u.Id == Krakow);
            Assert.Equal("1261011", gmina.Teryt);

            var data = await client.GetDataByUnitAsync(Krakow, [PopulationTotal], [2025], ct);
            var value = Assert.Single(Assert.Single(data.Series).Values);
            Assert.True(value.Value > 700_000, "Krakow has more than 700,000 inhabitants.");
        }
        catch (QuotaExceededException ex)
        {
            Live.SkipOnQuota(ex);
        }
    }

    // The attribute table is baked in. If GUS adds an attribute id, a value could be mistaken for a number: fail loudly here.
    [Fact(Timeout = Live.TestTimeout)]
    public async Task Every_live_attribute_id_is_in_the_baked_in_table()
    {
        Live.SkipUnlessEnabled();
        using var http = Live.CreateHttpClient();
        var ct = TestContext.Current.CancellationToken;

        try
        {
            using var response = await http.GetAsync("https://bdl.stat.gov.pl/api/v1/attributes?format=json&lang=pl&page-size=100", ct);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var live = doc.RootElement.GetProperty("results").EnumerateArray().Select(a => a.GetProperty("id").GetInt32()).ToList();

            var known = new[] { 0, 1, 3, 4, 7, 9, 11, 13, 14, 15, 17, 20, 21, 50, 91, 94, 97, 98 };
            Assert.Empty(live.Except(known));
        }
        catch (QuotaExceededException ex)
        {
            Live.SkipOnQuota(ex);
        }
    }
}

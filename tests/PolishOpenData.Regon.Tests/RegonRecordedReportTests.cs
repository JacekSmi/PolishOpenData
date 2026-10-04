using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Bir.Tests;

// End to end through RegonClient with answers recorded from the GUS public test environment (legal entity GUS itself, 000331501):
// the login, the search, the BIR12OsPrawna full report and the data date are all served as captured bytes.
public class RegonRecordedReportTests
{
    private const string GusRegon = "000331501";

    private static FakeBirServer RecordedServer() => new FakeBirServer()
        .RecordedLogin()
        .Search("Regon", GusRegon, "search-regon-gus")
        .Report(GusRegon, "BIR12OsPrawna", "report-osprawna-gus")
        .GetValue("StanDanych", "getvalue-standanych");

    private static RegonClient CreateClient(FakeBirServer server) =>
        new(
            new HttpClient(server.Handler),
            new RegonClientOptions { Environment = RegonEnvironment.Test, RateLimit = RegonRateLimit.Off },
            new FakeTimeProvider(new DateTimeOffset(2026, 10, 4, 8, 0, 0, TimeSpan.Zero)));

    private static HashSet<string> OsPrawnaFieldNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in Fixture.Read("regon/bir12-field-names.tsv").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var cells = line.Split('\t');
            if (string.Equals(cells[0], "BIR12OsPrawna", StringComparison.Ordinal))
            {
                names.Add(cells[1]);
            }
        }

        return names;
    }

    [Fact]
    public async Task Recorded_report_of_gus_maps_end_to_end()
    {
        var server = RecordedServer();
        var client = CreateClient(server);

        var found = await client.FindByRegonAsync(Regon.Parse(GusRegon), TestContext.Current.CancellationToken);
        var row = Assert.Single(found.Value);
        var result = await client.GetDetailsAsync(row, RegonInclude.None, TestContext.Current.CancellationToken);

        Assert.Equal(["Zaloguj", "DaneSzukajPodmioty", "DanePobierzPelnyRaport"], server.Calls.Select(c => c.Operation));
        Assert.All(server.Calls.Skip(1), c => Assert.Equal("0123456789abcdefghij", c.Sid));   // the sid of the recorded login
        Assert.Equal(RegonEnvironment.Test, result.Environment);
        var details = Assert.Single(result.Value);
        Assert.Equal(RegonEntityType.LegalPerson, details.Type);
        Assert.Equal(Regon.Parse(GusRegon), details.Regon);
        Assert.Equal(Nip.Parse("5261040828"), details.Nip);
        Assert.Equal("GŁÓWNY URZĄD STATYSTYCZNY", details.Name);
        Assert.Equal("GUS", details.ShortName);
        Assert.Equal("2", details.LegalFormCode);
        Assert.Equal("Warszawa", details.Address.City);
        Assert.Equal("00-925", details.Address.PostalCode);
        Assert.Equal(new DateOnly(1975, 12, 15), details.FormationDate);
        Assert.Null(details.RegonEntryDate);
        Assert.Equal(0, details.LocalUnitCount);
        Assert.Null(details.Pkd);
        Assert.Null(details.LocalUnits);

        // 41 of the 55 fields of the report are filled for GUS on the test environment; every one is a BIR12OsPrawna name
        var known = OsPrawnaFieldNames();
        Assert.Equal(55, known.Count);
        Assert.Equal(41, details.Fields.Count);
        var unknown = details.Fields.Keys.Where(name => !known.Contains(name)).ToArray();
        Assert.True(unknown.Length == 0, "Fields that BIR12OsPrawna does not have: " + string.Join(", ", unknown));
    }

    [Fact]
    public async Task Recorded_data_date_is_parsed()
    {
        var server = RecordedServer();
        var client = CreateClient(server);

        var result = await client.GetDataDateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new DateOnly(2018, 10, 19), result.Value);
        Assert.Equal(RegonEnvironment.Test, result.Environment);
        Assert.Equal(new DateOnly(2018, 10, 19), result.DataAsOf);
    }
}

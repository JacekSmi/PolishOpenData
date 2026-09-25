using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PolishOpenData.BialaLista;

namespace PolishOpenData.SmokeTests;

/// <summary>The Biała Lista client against the live VAT whitelist API (gated and budgeted by <see cref="Live"/>).</summary>
public sealed class BialaListaLiveTests(BialaListaLiveFixture live) : IClassFixture<BialaListaLiveFixture>
{
    // PZU S.A.: a large public company already in the recorded batch fixture (tests/Fixtures/bialalista/search-nips-mixed.json),
    // where the live batch endpoint returned one subject for it. Its status there is Niezarejestrowany (insurance is
    // VAT-exempt), so only ORLEN's VAT status is asserted.
    private const string PzuNip = "5260251049";

    [Fact(Timeout = Live.TestTimeout)]
    public async Task BialaLista_search_and_check_of_orlen_match_the_model()
    {
        Live.SkipUnlessEnabled();
        try
        {
            var search = await live.FindOrlenAsync(TestContext.Current.CancellationToken);
            var subject = search.Value;
            Assert.NotNull(subject);
            Assert.Equal(VatStatus.Active, subject!.VatStatus);
            Assert.Empty(subject.UnknownFields);
            Assert.NotEmpty(subject.AccountNumbers);
            Assert.False(string.IsNullOrEmpty(search.RequestId));
            Assert.Empty(search.UnknownFields);

            var check = await live.Client.CheckBankAccountAsync(subject.Nip!.Value, subject.AccountNumbers[0], cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(check.Value);
            Assert.False(string.IsNullOrEmpty(check.RequestId));
            Assert.Empty(check.UnknownFields);
        }
        catch (QuotaExceededException ex)
        {
            Live.SkipOnQuota(ex);
        }
    }

    [Fact(Timeout = Live.TestTimeout)]
    public async Task BialaLista_regon_search_finds_the_same_subject_as_the_nip_search()
    {
        Live.SkipUnlessEnabled();
        try
        {
            var byNip = (await live.FindOrlenAsync(TestContext.Current.CancellationToken)).Value;
            Assert.NotNull(byNip);
            Assert.NotNull(byNip!.Regon);

            var search = await live.Client.FindByRegonAsync(byNip.Regon!.Value, cancellationToken: TestContext.Current.CancellationToken);
            var subject = search.Value;
            Assert.NotNull(subject);
            Assert.Equal(Nip.Parse(Live.OrlenNip), subject!.Nip);
            Assert.Equal(byNip.Regon, subject.Regon);
            Assert.Equal(byNip.Name, subject.Name);
            Assert.Empty(subject.UnknownFields);
            Assert.False(string.IsNullOrEmpty(search.RequestId));
            Assert.Empty(search.UnknownFields);
        }
        catch (QuotaExceededException ex)
        {
            Live.SkipOnQuota(ex);
        }
    }

    [Fact(Timeout = Live.TestTimeout)]
    public async Task BialaLista_batch_search_matches_each_nip_to_its_subject()
    {
        Live.SkipUnlessEnabled();
        Nip[] nips = [Nip.Parse(Live.OrlenNip), Nip.Parse(PzuNip)];
        try
        {
            var batch = await live.Client.FindByNipsAsync(nips, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(2, batch.Value.Count);
            foreach (var nip in nips)
            {
                // Entries are not returned in request order: match on the identifier.
                var entry = Assert.Single(batch.Value, e => string.Equals(e.Identifier, nip.ToString(), StringComparison.Ordinal));
                Assert.Null(entry.Error);
                Assert.NotEmpty(entry.Subjects);
                Assert.All(entry.Subjects, s =>
                {
                    Assert.Equal(nip, s.Nip);
                    Assert.Empty(s.UnknownFields);
                });
            }

            var orlen = Assert.Single(batch.Value, e => string.Equals(e.Identifier, Live.OrlenNip, StringComparison.Ordinal));
            Assert.Equal(VatStatus.Active, orlen.Subjects[0].VatStatus);
            Assert.False(string.IsNullOrEmpty(batch.RequestId));
            Assert.Empty(batch.UnknownFields);
        }
        catch (QuotaExceededException ex)
        {
            Live.SkipOnQuota(ex);
        }
    }
}

/// <summary>One client for the class, and one ORLEN NIP search shared by the tests that need it (saves a search per run).</summary>
public sealed class BialaListaLiveFixture : IDisposable
{
    private readonly HttpClient _http = Live.CreateHttpClient();
    private Task<BialaListaResult<VatSubject?>>? _orlen;

    public BialaListaLiveFixture() => Client = new BialaListaClient(_http);

    public BialaListaClient Client { get; }

    /// <summary>
    /// The ORLEN NIP search, sent once (the tests run one at a time). A cancelled search (the first caller's test timed
    /// out, or the HTTP timeout ran out) is sent again, so the next test reports its own result instead of that
    /// cancellation; any other failure is kept and rethrown to every caller.
    /// </summary>
    public Task<BialaListaResult<VatSubject?>> FindOrlenAsync(CancellationToken cancellationToken)
    {
        if (_orlen is { IsCanceled: true })
        {
            _orlen = null;
        }

        return _orlen ??= Client.FindByNipAsync(Nip.Parse(Live.OrlenNip), cancellationToken: cancellationToken);
    }

    public void Dispose() => _http.Dispose();
}

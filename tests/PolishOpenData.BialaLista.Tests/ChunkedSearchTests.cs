using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using PolishOpenData.BialaLista;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.BialaLista.Tests;

public class ChunkedSearchTests
{
    // Generates distinct valid NIPs: 9-digit prefixes with a computed check digit (remainder 10 skipped).
    private static List<Nip> ValidNips(int count)
    {
        int[] weights = [6, 5, 7, 2, 3, 4, 5, 6, 7];
        var result = new List<Nip>();
        for (var prefix = 100000000; result.Count < count; prefix++)
        {
            var digits = prefix.ToString(CultureInfo.InvariantCulture);
            var sum = 0;
            for (var i = 0; i < 9; i++)
            {
                sum += (digits[i] - '0') * weights[i];
            }

            if (sum % 11 != 10)
            {
                result.Add(Nip.Parse(digits + (sum % 11).ToString(CultureInfo.InvariantCulture)));
            }
        }

        return result;
    }

    private static HttpResponseMessage EchoEntries(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        var identifiers = path.Substring(path.LastIndexOf('/') + 1).Split(',');
        var entries = string.Join(",", identifiers.Select(id => "{\"identifier\":\"" + id + "\",\"subjects\":[]}"));
        var requestId = "R" + identifiers.Length.ToString(CultureInfo.InvariantCulture) + "-" + identifiers[0];
        return StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            "{\"result\":{\"entries\":[" + entries + "],\"requestId\":\"" + requestId + "\",\"requestDateTime\":\"24-09-2026 10:00:00\"}}");
    }

    [Fact]
    public async Task Splits_into_requests_of_at_most_thirty_and_keeps_every_request_id()
    {
        var stub = new StubHttpMessageHandler(EchoEntries);
        var client = new BialaListaClient(new HttpClient(stub), timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 24, 8, 0, 0, TimeSpan.Zero)));
        var nips = ValidNips(65);
        nips.Add(nips[0]);   // duplicates are sent once

        var results = new List<BialaListaResult<IReadOnlyList<VatBatchEntry>>>();
        await foreach (var result in client.FindByNipsChunkedAsync(nips, cancellationToken: TestContext.Current.CancellationToken))
        {
            results.Add(result);
        }

        Assert.Equal(3, stub.RequestUris.Count);
        Assert.Equal(new[] { 30, 30, 5 }, results.Select(r => r.Value.Count));
        Assert.Equal(3, results.Select(r => r.RequestId).Distinct().Count());
        Assert.Equal(65, results.SelectMany(r => r.Value).Select(e => e.Identifier).Distinct().Count());
    }

    [Theory]
    [InlineData(30, new[] { 30 })]
    [InlineData(60, new[] { 30, 30 })]
    public async Task Exact_multiples_of_thirty_send_no_extra_request(int count, int[] expectedSizes)
    {
        var stub = new StubHttpMessageHandler(EchoEntries);
        var client = new BialaListaClient(new HttpClient(stub), timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 24, 8, 0, 0, TimeSpan.Zero)));
        var nips = ValidNips(count);

        var results = new List<BialaListaResult<IReadOnlyList<VatBatchEntry>>>();
        await foreach (var result in client.FindByNipsChunkedAsync(nips, cancellationToken: TestContext.Current.CancellationToken))
        {
            results.Add(result);
        }

        Assert.Equal(expectedSizes.Length, stub.RequestUris.Count);
        Assert.Equal(expectedSizes, results.Select(r => r.Value.Count));
        Assert.Equal(nips.Select(n => n.ToString()), results.SelectMany(r => r.Value).Select(e => e.Identifier));
    }

    [Fact]
    public async Task Empty_input_sends_nothing()
    {
        var stub = new StubHttpMessageHandler(EchoEntries);
        var client = new BialaListaClient(new HttpClient(stub));
        await foreach (var _ in client.FindByNipsChunkedAsync([], cancellationToken: TestContext.Current.CancellationToken))
        {
            Assert.Fail("No results expected.");
        }

        Assert.Empty(stub.RequestUris);
    }
}

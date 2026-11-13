using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Bdl.Tests;

public class BdlClientDataTests
{
    private static BdlClient Client(StubHttpMessageHandler handler) => new(new HttpClient(handler));

    private static string Query(StubHttpMessageHandler handler, int index = 0) => handler.RequestUris[index].Query;

    private static string Page(int total, int count, int firstId)
    {
        var sb = new StringBuilder("{\"totalRecords\":" + total + ",\"variableId\":72305,\"measureUnitId\":26,\"results\":[");
        for (var i = 0; i < count; i++)
        {
            sb.Append(i == 0 ? "" : ",").Append("{\"id\":\"").Append((firstId + i).ToString("D12", CultureInfo.InvariantCulture)).Append("\",\"name\":\"u").Append(firstId + i).Append("\",\"values\":[{\"year\":\"2025\",\"val\":1,\"attrId\":1}]}");
        }

        return sb.Append("]}").ToString();
    }

    [Fact]
    public async Task ByUnit_RepeatsVarIdAndYear()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "bdl/data-by-unit-krakow.json"));

        var data = await Client(handler).GetDataByUnitAsync("011212161011", [72305, 60559, 1], [2024, 2025], TestContext.Current.CancellationToken);

        Assert.Equal(3, data.Series.Count);
        var q = Query(handler);
        Assert.Contains("var-id=72305&var-id=60559&var-id=1", q, StringComparison.Ordinal);
        Assert.Contains("year=2024&year=2025", q, StringComparison.Ordinal);
        Assert.Contains("page-size=100", q, StringComparison.Ordinal);
        Assert.Contains("/data/by-unit/011212161011?", handler.RequestUris.Single().AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ByUnit_WithoutYears_SendsNoYear()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "bdl/data-by-unit-krakow.json"));

        _ = await Client(handler).GetDataByUnitAsync("011212161011", [72305], cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain("year=", Query(handler), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ByUnit_DedupesIds()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "bdl/data-by-unit-krakow.json"));

        _ = await Client(handler).GetDataByUnitAsync("011212161011", [72305, 72305, 60559], cancellationToken: TestContext.Current.CancellationToken);

        var q = Query(handler);
        Assert.Equal(2, q.Split(["var-id="], StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public async Task ByUnit_RejectsZeroOrOver100Ids()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "bdl/data-by-unit-krakow.json"));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Client(handler).GetDataByUnitAsync("011212161011", [], cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Client(handler).GetDataByUnitAsync("011212161011", Enumerable.Range(1, 101).ToArray(), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task ByUnit_UnknownUnit_EmptySeries()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "bdl/empty-unit.json"));

        var data = await Client(handler).GetDataByUnitAsync("999999999999", [72305], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(data.Series);
    }

    [Fact]
    public async Task ByVariable_PagesUntilMaxPages_AndReportsTruncation()
    {
        var handler = new StubHttpMessageHandler(r => StubHttpMessageHandler.Json(HttpStatusCode.OK,
            r.RequestUri!.Query.Contains("page=0", StringComparison.Ordinal) ? Page(250, 100, 0) : Page(250, 100, 100)));

        var data = await Client(handler).GetDataByVariableAsync(72305, BdlLevel.Gmina, maxPages: 2, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, handler.RequestUris.Count);
        Assert.Contains("page=0", Query(handler, 0), StringComparison.Ordinal);
        Assert.Contains("page=1", Query(handler, 1), StringComparison.Ordinal);
        Assert.Equal(200, data.Units.Count);
        Assert.Equal(250, data.TotalRecords);
        Assert.True(data.Truncated);
    }

    [Fact]
    public async Task ByVariable_AllReadInOnePage_IsNotTruncated()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK, Page(3, 3, 0)));

        var data = await Client(handler).GetDataByVariableAsync(72305, BdlLevel.Gmina, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(handler.RequestUris);
        Assert.False(data.Truncated);
        Assert.Equal(3, data.Units.Count);
    }

    [Fact]
    public async Task ByVariable_StopsOnAnEmptyPage()
    {
        var handler = new StubHttpMessageHandler(r => StubHttpMessageHandler.Json(HttpStatusCode.OK,
            r.RequestUri!.Query.Contains("page=0", StringComparison.Ordinal) ? Page(500, 100, 0) : Page(500, 0, 0)));

        var data = await Client(handler).GetDataByVariableAsync(72305, BdlLevel.Gmina, maxPages: 10, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, handler.RequestUris.Count);
        Assert.Equal(100, data.Units.Count);
        Assert.True(data.Truncated);
    }

    [Fact]
    public async Task ByVariable_RejectsMaxPagesBelowOne()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK, Page(1, 1, 0)));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Client(handler).GetDataByVariableAsync(72305, BdlLevel.Gmina, maxPages: 0, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task ByVariable_SendsParentLevelAndYears()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "bdl/data-by-variable-72305.json"));

        var data = await Client(handler).GetDataByVariableAsync(72305, BdlLevel.Gmina, "011212100000", [2025], cancellationToken: TestContext.Current.CancellationToken);

        var q = Query(handler);
        Assert.Contains("unit-parent-id=011212100000", q, StringComparison.Ordinal);
        Assert.Contains("unit-level=6", q, StringComparison.Ordinal);
        Assert.Contains("year=2025", q, StringComparison.Ordinal);
        Assert.Contains("/data/by-variable/72305?", handler.RequestUris.Single().AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal("Kraków", data.Units.Single().Name);
        Assert.False(data.Truncated);
    }
}

using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using PolishOpenData.Demo.Http;
using PolishOpenData.Geo;
using PolishOpenData.Tests.Shared;
using PolishOpenData.Uldk;

namespace PolishOpenData.Demo.Tests;

public class StripUserAgentHandlerTests
{
    // The library adds User-Agent and Accept to every request; the handler sits between it and the browser's fetch.
    private static async Task<(StubHttpMessageHandler Stub, string? Accept)> SendOneRequestAsync()
    {
        string? accept = null;
        var stub = new StubHttpMessageHandler(request =>
        {
            accept = request.Headers.Accept.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Fixture.Read("uldk/parcel-146510_8.0507.41_1.txt"), Encoding.UTF8, "text/plain"),
            };
        });
        var client = new UldkClient(new HttpClient(new StripUserAgentHandler { InnerHandler = stub }));

        var parcel = await client.GetParcelByIdAsync(ParcelId.Parse("146510_8.0507.41/1"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(parcel); // the request went through the handler and the answer was read
        return (stub, accept);
    }

    [Fact]
    public async Task Handler_removes_the_user_agent()
    {
        var (stub, _) = await SendOneRequestAsync();

        Assert.Single(stub.UserAgents);
        Assert.All(stub.UserAgents, userAgent => Assert.Equal(string.Empty, userAgent));
        Assert.All(stub.Requests, request => Assert.Null(request.UserAgent));
    }

    [Fact]
    public async Task Other_headers_are_kept()
    {
        var (_, accept) = await SendOneRequestAsync();

        Assert.Equal("application/json", accept);
    }
}

using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using PolishOpenData.Demo;
using PolishOpenData.Demo.Http;
using PolishOpenData.Rcn;
using PolishOpenData.Uldk;
using PolishOpenData.Uug;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

// One HttpClient for the three clients; they build absolute URIs, so it needs no BaseAddress. AddUldkClient and its siblings are not
// used: they set HttpClientHandler.MaxConnectionsPerServer, which throws PlatformNotSupportedException in the browser.
var http = new HttpClient(new StripUserAgentHandler { InnerHandler = new HttpClientHandler() }) { Timeout = TimeSpan.FromSeconds(30) };
builder.Services.AddSingleton(http);
builder.Services.AddSingleton<IUldkClient>(new UldkClient(http));
builder.Services.AddSingleton<IUugClient>(new UugClient(http));
// One page holds up to 1,000 rows, so a place costs a single RCN request and a truncated answer is still detected.
builder.Services.AddSingleton<IRcnClient>(new RcnClient(http, new RcnClientOptions { PageSize = 1001 }));

await builder.Build().RunAsync();

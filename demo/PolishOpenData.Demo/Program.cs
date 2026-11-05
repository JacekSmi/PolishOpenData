using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using PolishOpenData.Demo;
using PolishOpenData.Demo.Http;
using PolishOpenData.Demo.Map;
using PolishOpenData.Demo.Search;
using PolishOpenData.Demo.State;
using PolishOpenData.Internal;
using PolishOpenData.Rcn;
using PolishOpenData.Uldk;
using PolishOpenData.Uug;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

// One HttpClient for the three clients; they build absolute URIs, so it needs no BaseAddress. AddUldkClient and its siblings are not
// used: they set HttpClientHandler.MaxConnectionsPerServer, which throws PlatformNotSupportedException in the browser.
var http = new HttpClient(new StripUserAgentHandler { InnerHandler = new HttpClientHandler() }) { Timeout = TimeSpan.FromSeconds(30) };
var uldk = new UldkClient(http);
var uug = new UugClient(http);
// One page holds up to 1,000 rows, so a place costs a single RCN request and a truncated answer is still detected.
var rcn = new RcnClient(http, new RcnClientOptions { PageSize = 1001 });
builder.Services.AddSingleton(http);
builder.Services.AddSingleton<IUldkClient>(uldk);
builder.Services.AddSingleton<IUugClient>(uug);
builder.Services.AddSingleton<IRcnClient>(rcn);

// One page, one user: the search, the controller (and its session cache) and the map live as long as the page.
// "Today" is the date in Warsaw, as in property_prices; WarsawTime falls back to a built-in zone when the browser runtime has no time-zone data.
builder.Services.AddSingleton(new PlaceSearch(uldk, rcn, () => WarsawTime.Today(TimeProvider.System)));
builder.Services.AddSingleton(new AddressSearch(uug));
builder.Services.AddSingleton<PlaceController>();
builder.Services.AddSingleton<MapInterop>();

await builder.Build().RunAsync();

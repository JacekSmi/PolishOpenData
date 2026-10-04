using System;

namespace PolishOpenData.Bir;

/// <summary>The GUS BIR service a REGON client talks to.</summary>
public enum RegonEnvironment
{
    /// <summary>The production service (<c>wyszukiwarkaregon.stat.gov.pl</c>). Needs your own free GUS key.</summary>
    Production = 0,

    /// <summary>
    /// GUS's public test service (<c>wyszukiwarkaregontest.stat.gov.pl</c>): an old, anonymised copy of the register.
    /// It works only with GUS's published test key, which the client always uses here: a key of your own is neither
    /// needed nor sent to the test service.
    /// </summary>
    Test = 1,
}

/// <summary>How the client limits its own request rate, so that GUS is not overloaded.</summary>
public enum RegonRateLimit
{
    /// <summary>The strictest published window at every time of day: 3 requests per second, 120 per minute, 6,000 per hour. The default.</summary>
    Strictest = 0,

    /// <summary>The three published windows, chosen by the hour in Europe/Warsaw (the time zone is not stated by GUS; Polish time is assumed).</summary>
    TimeOfDay = 1,

    /// <summary>No local limit. The limits of GUS still apply to you.</summary>
    Off = 2,
}

/// <summary>Options for the REGON client.</summary>
public sealed class RegonClientOptions
{
    /// <summary>
    /// Your own GUS BIR user key. There is no default, and the library never reads environment variables: pass the
    /// key from user secrets, a vault or a variable of your choice. The key is never logged, never put in a URL or an
    /// exception message and never printed by <c>ToString()</c>. Required with <see cref="RegonEnvironment.Production"/>.
    /// Ignored with <see cref="RegonEnvironment.Test"/>, which always uses GUS's published test key; your key is never
    /// sent to the test service.
    /// </summary>
    public string? UserKey { get; set; }

    /// <summary>Which GUS service to call. Default <see cref="RegonEnvironment.Production"/>. Every result says which one it came from.</summary>
    public RegonEnvironment Environment { get; set; }

    /// <summary>
    /// Overrides the service address (for example a fake in a test). <c>null</c> selects the address of <see cref="Environment"/>.
    /// Must be an absolute <c>https</c> URI, because the key travels in the login request. The address of the test service
    /// is accepted only with <see cref="RegonEnvironment.Test"/> and the address of the production service only with
    /// <see cref="RegonEnvironment.Production"/>, so that a result's environment label always matches the service called.
    /// </summary>
    public Uri? BaseAddress { get; set; }

    /// <summary>How long a session id is reused before the client logs in again. Default 30 minutes (GUS ends a session after 60).</summary>
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>The local request limiter. Default <see cref="RegonRateLimit.Strictest"/>.</summary>
    public RegonRateLimit RateLimit { get; set; }

    /// <summary>
    /// The longest a call waits for the per-second window to clear before it fails with
    /// <see cref="QuotaExceededException"/>. Default 5 seconds. Per-minute and per-hour exhaustion never waits.
    /// </summary>
    public TimeSpan MaxRateLimitWait { get; set; } = TimeSpan.FromSeconds(5);

    // GUS's published test key (GUS-Regon-UslugaBIRver1.2 manual p.5; research-gus-bir/api-facts.md section 3). Environment.Test always uses it and ignores UserKey.
    internal const string TestUserKey = "abcde12345abcde12345";

    internal static readonly Uri ProductionAddress = new("https://wyszukiwarkaregon.stat.gov.pl/wsBIR/UslugaBIRzewnPubl.svc");

    internal static readonly Uri TestAddress = new("https://wyszukiwarkaregontest.stat.gov.pl/wsBIR/UslugaBIRzewnPubl.svc");

    internal Uri ResolveAddress() =>
        BaseAddress ?? (Environment == RegonEnvironment.Test ? TestAddress : ProductionAddress);

    internal string ResolveUserKey()
    {
        // The test service knows only the published key, and GUS logs the content of every request: an application that
        // keeps its own key configured and flips Environment to Test must not transmit that key.
        if (Environment == RegonEnvironment.Test)
        {
            return TestUserKey;
        }

        if (UserKey is { } key && !string.IsNullOrWhiteSpace(key))
        {
            return key;
        }

        throw new InvalidOperationException(
            "RegonClientOptions.UserKey is required for the production REGON service. Apply for a free key at GUS (regon_bir@stat.gov.pl), or set RegonClientOptions.Environment to RegonEnvironment.Test to use GUS's public test service.");
    }

    // The label of every result. A BaseAddress that points at one of the two GUS services must agree with Environment,
    // otherwise test data would be labelled Production (or the other way round) and the key would reach the wrong host.
    internal RegonEnvironment ResolveEnvironment()
    {
        var host = ResolveAddress().Host;
        if (Environment == RegonEnvironment.Production && string.Equals(host, TestAddress.Host, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "RegonClientOptions.BaseAddress points at the GUS test service while RegonClientOptions.Environment is Production. Set Environment to RegonEnvironment.Test or remove BaseAddress.");
        }

        if (Environment == RegonEnvironment.Test && string.Equals(host, ProductionAddress.Host, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "RegonClientOptions.BaseAddress points at the GUS production service while RegonClientOptions.Environment is Test. Set Environment to RegonEnvironment.Production or remove BaseAddress.");
        }

        return Environment;
    }

    internal void Validate()
    {
        if (SessionLifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(null, SessionLifetime, "RegonClientOptions.SessionLifetime must be greater than zero.");
        }

        if (MaxRateLimitWait < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(null, MaxRateLimitWait, "RegonClientOptions.MaxRateLimitWait must not be negative.");
        }

        if (BaseAddress is { } address && (!address.IsAbsoluteUri || !string.Equals(address.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)))
        {
            throw new ArgumentException("RegonClientOptions.BaseAddress must be an absolute https URI.");
        }

        if (Environment is not (RegonEnvironment.Production or RegonEnvironment.Test))
        {
            throw new ArgumentOutOfRangeException(null, Environment, "RegonClientOptions.Environment is not a defined RegonEnvironment value.");
        }

        if (RateLimit is not (RegonRateLimit.Strictest or RegonRateLimit.TimeOfDay or RegonRateLimit.Off))
        {
            throw new ArgumentOutOfRangeException(null, RateLimit, "RegonClientOptions.RateLimit is not a defined RegonRateLimit value.");
        }
    }
}

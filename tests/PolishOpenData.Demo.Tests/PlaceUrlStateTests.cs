using System;
using System.Linq;
using PolishOpenData.Demo.State;
using PolishOpenData.Geo;

namespace PolishOpenData.Demo.Tests;

public class PlaceUrlStateTests
{
    private const string Position = "lat=52.22850&lon=21.01720";
    private const string Warsaw = "#" + Position + "&r=500&m=24";

    // Polish letters as escapes: L with stroke, o with acute, z with acute (the word "Lodz" with its diacritics).
    private const string Lodz = "\u0141\u00F3d\u017A";

    // Four-byte UTF-8 (a surrogate pair in UTF-16): U+1F600.
    private const string Emoji = "\U0001F600";
    private const string EmojiEscaped = "%F0%9F%98%80";

    private static PlaceUrlState Parse(string fragment)
    {
        Assert.True(PlaceUrlState.TryParse(fragment, out var state), $"expected '{fragment}' to parse");
        return state;
    }

    private static PlaceUrlState Sample(string? label = "Warszawa, Krucza 50", int radius = 500, int months = 24) =>
        new(new Wgs84Point(52.2285, 21.0172), radius, months, label);

    [Fact]
    public void Round_trips()
    {
        foreach (var radius in PlaceUrlState.Radii)
        {
            foreach (var months in PlaceUrlState.Months)
            {
                foreach (var label in new[] { null, "Warszawa, Krucza 50", Lodz + ", Piotrkowska 1 " + Emoji, "a&b=c#d%e+f" })
                {
                    var state = Sample(label, radius, months);

                    Assert.Equal(state, Parse(state.ToFragment()));
                }
            }
        }
    }

    [Fact]
    public void ToFragment_writes_the_label_capped_to_100_chars_without_splitting_a_surrogate_pair()
    {
        const string Prefix = "#lat=52.22850&lon=21.01720&r=500&m=24&q=";

        // The exact text pins the cap in ToFragment itself (TryParse caps too, so a round trip alone would not notice).
        Assert.Equal(Prefix + new string('a', 100), Sample(new string('a', 150)).ToFragment());
        Assert.Equal(Prefix + new string('a', 99), Sample(new string('a', 99) + Emoji).ToFragment());
        Assert.Equal(Prefix + new string('a', 98) + EmojiEscaped, Sample(new string('a', 98) + Emoji + "b").ToFragment());
        Assert.Equal(Sample(new string('a', 100)), Parse(Sample(new string('a', 150)).ToFragment()));
    }

    [Fact]
    public void ToFragment_removes_control_and_format_characters_inside_a_label()
    {
        // NUL, tab, a bidi override (U+202E) and a line separator (U+2028) inside a label that is not blank, plus spaces around it.
        var label = "  Kru\u0000cza\t\u202E 5\u20280  ";

        Assert.Equal("#lat=52.22850&lon=21.01720&r=500&m=24&q=Krucza%2050", Sample(label).ToFragment());
    }

    [Fact]
    public void ToFragment_writes_the_documented_format()
    {
        Assert.Equal("#lat=52.22850&lon=21.01720&r=500&m=24&q=Warszawa%2C%20Krucza%2050", Sample().ToFragment());
        Assert.Equal("#lat=52.22850&lon=21.01720&r=2000&m=60&q=%C5%81%C3%B3d%C5%BA", Sample(Lodz, 2000, 60).ToFragment());

        // Five decimals, rounded (about a metre), never more.
        var rounded = new PlaceUrlState(new Wgs84Point(52.2229075, 21.0230574), 250, 12, null);
        Assert.Equal("#lat=52.22291&lon=21.02306&r=250&m=12", rounded.ToFragment());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n\u0007")]
    public void ToFragment_leaves_out_q_when_there_is_no_label(string? label) =>
        Assert.Equal(Warsaw, Sample(label).ToFragment());

    [Fact]
    public void ToFragment_never_throws_on_an_odd_label()
    {
        // A lone surrogate is written as U+FFFD by the escaper; the point is that the link is still produced and still parses.
        var fragment = Sample("a\uD800b").ToFragment();

        Assert.Equal("a\uFFFDb", Parse(fragment).Label);
    }

    // The invariant culture writes "52.22850"; pl-PL would write "52,22850", and en-US groups nothing here but still tests the decimal point.
    // The pl-PL case needs ICU on the machine that runs the tests (the demo itself runs with InvariantGlobalization).
    [Theory]
    [InlineData("pl-PL")]
    [InlineData("en-US")]
    [InlineData("")]
    public void Fragment_has_no_culture_dependence(string culture)
    {
        var (fragment, parsed) = CultureScope.Run(culture, () =>
        {
            var written = Sample().ToFragment();
            return (written, PlaceUrlState.TryParse(written, out var state) ? state : null);
        });

        Assert.Equal("#lat=52.22850&lon=21.01720&r=500&m=24&q=Warszawa%2C%20Krucza%2050", fragment);
        Assert.Equal(Sample(), parsed);
    }

    [Theory]
    [InlineData("#lat=21.0172&lon=52.2285")] // swapped axes
    [InlineData("#lat=60&lon=21.0172")]
    [InlineData("#lat=52.2285&lon=30")]
    [InlineData("#lat=48.89999&lon=21.0172")] // just south of the range
    [InlineData("#lat=55.00001&lon=21.0172")]
    [InlineData("#lat=52.2285&lon=13.99999")]
    [InlineData("#lat=52.2285&lon=24.20001")]
    [InlineData("#lat=-52.2285&lon=21.0172")]
    [InlineData("#lat=+52.2285&lon=21.0172")] // no sign at all: every coordinate in Poland is positive
    [InlineData("#lat=NaN&lon=21.0172")]
    [InlineData("#lat=Infinity&lon=21.0172")]
    [InlineData("#lat=52.2285&lon=-Infinity")]
    [InlineData("#lat=5.22285e1&lon=21.0172")] // an exponent is not a coordinate
    [InlineData("#lat=52,2285&lon=21,0172")] // a decimal comma is not accepted
    [InlineData("#lat=%2052.2285&lon=21.0172")] // numbers are not percent-decoded
    [InlineData("#lat= 52.2285&lon=21.0172")]
    [InlineData("#lat=52.2285 &lon=21.0172")]
    [InlineData("#lat=&lon=21.0172")]
    [InlineData("#lat=52.2285&lon=")]
    [InlineData("#lat=north&lon=east")]
    [InlineData("#lon=21.0172")]
    [InlineData("#lat=52.2285")]
    public void Rejects_points_outside_Poland_and_swapped_axes(string fragment)
    {
        Assert.False(PlaceUrlState.TryParse(fragment, out var state));
        Assert.Null(state);
    }

    [Theory]
    [InlineData("#lat=48.9&lon=14.0")]
    [InlineData("#lat=55.0&lon=24.2")]
    [InlineData("#lat=52&lon=21")]
    public void Accepts_the_edges_of_the_range_and_plain_numbers(string fragment) =>
        Assert.True(PlaceUrlState.TryParse(fragment, out _));

    [Theory]
    [InlineData("r=0")]
    [InlineData("r=100")]
    [InlineData("r=300")]
    [InlineData("r=-500")]
    [InlineData("r=500.0")]
    [InlineData("r=5e2")]
    [InlineData("r=%20500")]
    [InlineData("r=+500")]
    [InlineData("r=abc")]
    [InlineData("r=")]
    [InlineData("r=3000")]
    [InlineData("r=99999999999999999999")]
    [InlineData("m=0")]
    [InlineData("m=6")]
    [InlineData("m=25")]
    [InlineData("m=120")]
    [InlineData("m=-24")]
    [InlineData("m=")]
    [InlineData("m=two")]
    [InlineData("r=300&m=24")]
    [InlineData("r=500&m=25")]
    public void Rejects_unknown_radius_and_months(string parameters)
    {
        Assert.False(PlaceUrlState.TryParse("#" + Position + "&" + parameters, out var state));
        Assert.Null(state);
    }

    [Fact]
    public void Every_allowed_radius_and_period_is_accepted()
    {
        Assert.Equal([250, 500, 1000, 2000], PlaceUrlState.Radii);
        Assert.Equal([12, 24, 60], PlaceUrlState.Months);
        Assert.Equal(500, PlaceUrlState.DefaultRadius);
        Assert.Equal(24, PlaceUrlState.DefaultMonths);

        foreach (var radius in PlaceUrlState.Radii)
        {
            foreach (var months in PlaceUrlState.Months)
            {
                var state = Parse($"#{Position}&r={radius}&m={months}");

                Assert.Equal(radius, state.RadiusMeters);
                Assert.Equal(months, state.PeriodMonths);
            }
        }
    }

    [Theory]
    [InlineData("#lat=52.22850&lon=21.01720", 500, 24)]
    [InlineData("#lat=52.22850&lon=21.01720&r=2000", 2000, 24)]
    [InlineData("#lat=52.22850&lon=21.01720&m=60", 500, 60)]
    [InlineData("#lat=52.22850&lon=21.01720&q=Krucza", 500, 24)]
    public void Missing_radius_and_months_use_defaults(string fragment, int radius, int months)
    {
        var state = Parse(fragment);

        Assert.Equal(radius, state.RadiusMeters);
        Assert.Equal(months, state.PeriodMonths);
        Assert.Equal(new Wgs84Point(52.2285, 21.0172), state.Point);
    }

    [Theory]
    [InlineData("#m=60&q=Krucza&lon=21.01720&lat=52.22850&r=1000")]
    [InlineData("m=60&q=Krucza&lon=21.01720&lat=52.22850&r=1000")]
    [InlineData("#lat=52.22850&lon=21.01720&r=1000&m=60&q=Krucza&utm_source=mail&=&novalue&&x=1&x=2")]
    [InlineData("lat=52.22850&unknown=1&lon=21.01720&r=1000&m=60&q=Krucza")]
    public void Parameters_come_in_any_order_with_or_without_hash_and_unknown_ones_are_ignored(string fragment)
    {
        var state = Parse(fragment);

        Assert.Equal(new PlaceUrlState(new Wgs84Point(52.2285, 21.0172), 1000, 60, "Krucza"), state);
    }

    [Theory]
    [InlineData("#LAT=52.22850&lon=21.01720")] // names are case-sensitive
    [InlineData("#lat=52.22850&Lon=21.01720")]
    [InlineData("##lat=52.22850&lon=21.01720")] // only one leading hash is dropped
    [InlineData("?lat=52.22850&lon=21.01720")]
    public void Parameter_names_are_exact(string fragment) =>
        Assert.False(PlaceUrlState.TryParse(fragment, out _));

    // Decision: a repeated known parameter is ambiguous, so the whole fragment fails (even when the values agree).
    [Theory]
    [InlineData("#lat=52.22850&lat=52.22850&lon=21.01720")]
    [InlineData("#lat=52.22850&lon=21.01720&lon=22")]
    [InlineData("#lat=52.22850&lon=21.01720&r=500&r=500")]
    [InlineData("#lat=52.22850&lon=21.01720&m=24&m=12")]
    [InlineData("#lat=52.22850&lon=21.01720&q=a&q=b")]
    public void Repeated_known_parameters_fail(string fragment) =>
        Assert.False(PlaceUrlState.TryParse(fragment, out _));

    public static TheoryData<string, string?> LabelCases => new()
    {
        // Plain, escaped and literal text.
        { "Warszawa%2C%20Krucza%2050", "Warszawa, Krucza 50" },
        { "Warszawa,%20Krucza%2050", "Warszawa, Krucza 50" },
        { "a+b", "a+b" },
        { "a%2Bb%26c%3Dd%23e%25f", "a+b&c=d#e%f" },
        { "%C5%81%C3%B3d%C5%BA", Lodz },
        { "%c5%81%c3%b3d%c5%ba", Lodz },
        { Lodz, Lodz },
        { Emoji + "x", Emoji + "x" },

        // Trimmed.
        { "%20%20Krucza%2050%20%20", "Krucza 50" },
        { "Warszawa%2C%20%20%20Krucza", "Warszawa,   Krucza" },

        // Control characters are removed: C0 (NUL, tab, line feed, DEL), C1 (U+0085), and invisible formatting (bidi override, line separator).
        { "Kru%00cza%0A%09%0D%7F50", "Krucza50" },
        { "a%C2%85b", "ab" },
        { "%E2%80%AEabc", "abc" },
        { "a%E2%80%A8b%E2%80%A9c", "abc" },
        { "%0A%20Krucza%20%0A", "Krucza" },

        // Nothing left means no label.
        { "", null },
        { "%20%20", null },
        { "%00%0A%09", null },

        // A malformed percent sequence or invalid UTF-8 makes the label null; the rest of the fragment is still good.
        { "abc%zz", null },
        { "abc%4", null },
        { "abc%", null },
        { "%%", null },
        { "%FF", null },
        { "%C5", null },
        { "%C0%AF", null },
        { "%ED%A0%80", null },
    };

    // A land-registry (KW) number is never shown or echoed: a KW-shaped label is dropped (the rule of AddressSearch's refusal), the rest of the link stands.
    [Theory]
    [InlineData("WA1M/00012345/1")]
    [InlineData("WA1M%2F00012345%2F1")]
    [InlineData("wa1m%2F00012345%2F1")]
    [InlineData("WA1M%2000012345%201")]
    [InlineData("WA1M-00012345-1")]
    [InlineData("WA1M000123451")]
    [InlineData("%20WA1M%2F00012345%2F1%20")]
    public void A_KW_shaped_label_is_dropped(string rawQ)
    {
        var state = Parse($"#{Position}&r=250&m=12&q={rawQ}");

        Assert.Null(state.Label);
        Assert.Equal(250, state.RadiusMeters);
        Assert.Equal(12, state.PeriodMonths);
        Assert.Equal("#" + Position + "&r=250&m=12", state.ToFragment());
    }

    [Fact]
    public void A_KW_shaped_label_is_never_written_to_a_link() =>
        Assert.Equal(Warsaw, Sample(label: "WA1M/00012345/1").ToFragment());

    [Theory]
    [InlineData("00-025", "00-025")]
    [InlineData("Warszawa%2C%20Krucza%2050", "Warszawa, Krucza 50")]
    [InlineData("12345678", "12345678")]
    [InlineData("WA1M%2F00012345", "WA1M/00012345")]
    public void Labels_that_only_resemble_a_KW_are_kept(string rawQ, string expected) =>
        Assert.Equal(expected, Parse($"#{Position}&q={rawQ}").Label);

    [Theory]
    [MemberData(nameof(LabelCases))]
    public void Label_is_sanitised_and_capped(string rawQ, string? expected)
    {
        var state = Parse($"#{Position}&r=250&m=12&q={rawQ}");

        Assert.Equal(expected, state.Label);
        Assert.Equal(250, state.RadiusMeters);
        Assert.Equal(12, state.PeriodMonths);
    }

    [Fact]
    public void Label_is_cut_to_100_UTF16_chars_without_splitting_a_surrogate_pair()
    {
        var a99 = new string('a', 99);
        var a98 = new string('a', 98);

        // Exactly 100 stays; 101 is cut.
        Assert.Equal(new string('a', 100), Parse($"#{Position}&q={new string('a', 100)}").Label);
        Assert.Equal(new string('a', 100), Parse($"#{Position}&q={new string('a', 101)}").Label);
        Assert.Equal(new string('a', 100), Parse($"#{Position}&q={new string('a', 5000)}").Label);

        // A pair that would straddle the cut is dropped whole (99 chars), a pair that fits is kept (98 + 2 = 100).
        var straddling = Parse($"#{Position}&q={a99}{EmojiEscaped}").Label;
        Assert.Equal(a99, straddling);
        Assert.Equal(99, straddling!.Length);

        var fitting = Parse($"#{Position}&q={a98}{EmojiEscaped}b").Label;
        Assert.Equal(a98 + Emoji, fitting);
        Assert.Equal(100, fitting!.Length);

        // The cut can leave a space at the end; it is trimmed.
        Assert.Equal(a99, Parse($"#{Position}&q={a99}%20bbb").Label);

        // Control characters do not count towards the cap (they are removed first).
        Assert.Equal(new string('a', 100), Parse($"#{Position}&q={new string('a', 50)}%00{new string('a', 60)}").Label);
    }

    public static TheoryData<string> GarbageCases => new()
    {
        { "" },
        { " " },
        { "#" },
        { "##" },
        { "garbage" },
        { "#garbage" },
        { "#&&&" },
        { "#=&=&=" },
        { "#lat" },
        { "#lat=" },
        { "#lat=&lon=" },
        { "%%%" },
        { "#%" },
        { "lat=52.2285" },
        { "\uD800" },
        { "#lat=\uD800&lon=\uDC00" },
        { "#lat=52.22850&lon=21.01720&q=\uD800\uD800&r=\uDFFF" },
        { "#\0\0\0" },
        { "https://example.org/#lat=52.2285&lon=21.0172" },
        { new string('x', 100_000) },
        { "#" + new string('&', 100_000) },
        { "#lat=" + new string('9', 100_000) + "&lon=21" },
        { "#lat=52.2285&lon=21.0172&r=" + new string('5', 100_000) },
    };

    [Theory]
    [MemberData(nameof(GarbageCases))]
    public void Garbage_and_empty_fragments_fail(string fragment)
    {
        Assert.False(PlaceUrlState.TryParse(fragment, out var state));
        Assert.Null(state);
    }

    [Fact]
    public void A_null_fragment_fails()
    {
        Assert.False(PlaceUrlState.TryParse(null, out var state));
        Assert.Null(state);
    }

    [Fact]
    public void A_very_long_but_valid_label_is_capped_not_rejected()
    {
        var state = Parse($"#{Position}&q={string.Concat(Enumerable.Repeat("%C5%81", 50_000))}");

        Assert.Equal(new string('\u0141', 100), state.Label);
    }
}

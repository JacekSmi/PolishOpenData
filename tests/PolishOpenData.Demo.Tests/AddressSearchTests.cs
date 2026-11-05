using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PolishOpenData.Demo.Search;
using PolishOpenData.Geo;
using PolishOpenData.Tests.Shared;
using PolishOpenData.Uug;

namespace PolishOpenData.Demo.Tests;

public class AddressSearchTests
{
    private const string KwRefusal = "This demo never accepts land-registry (KW) numbers.";
    private const string NothingFound = "No address matched; check the spelling or add the town.";

    private static readonly Wgs84Point Krucza = new(52.2285, 21.0172);

    private static Task<AddressOutcome> ResolveAsync(AddressSearch search, string text) =>
        search.ResolveAsync(text, TestContext.Current.CancellationToken);

    // The real UugClient over a recorded answer: the matches are exactly what the library makes of GUGiK's JSON.
    private static (AddressSearch Search, StubHttpMessageHandler Stub) FromFixture(string fixture)
    {
        var stub = new StubHttpMessageHandler(_ => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "uug/" + fixture));
        return (new AddressSearch(new UugClient(new HttpClient(stub))), stub);
    }

    private static UugMatch Address(string street = "ulica Krucza", string number = "50", string? ulic = "10013", double accuracy = 0.76, Wgs84Point? at = null) =>
        new()
        {
            Kind = UugMatchKind.Address,
            City = "Warszawa",
            Street = street,
            Number = number,
            PostalCode = "00-025",
            Teryt = "146501",
            Simc = "0918123",
            Ulic = ulic,
            Location = at ?? Krucza,
            Accuracy = accuracy,
        };

    private static UugMatch Street() => new() { Kind = UugMatchKind.Street, City = "Warszawa", Street = "ulica Krucza", Teryt = "146501", Simc = "0918123", Ulic = "10013", Location = Krucza };

    [Fact]
    public async Task A_single_address_point_resolves()
    {
        var (search, stub) = FromFixture("address-krucza-50.json");

        var outcome = await ResolveAsync(search, "Warszawa, Krucza 50");

        Assert.Equal(AddressOutcomeKind.Resolved, outcome.Kind);
        Assert.Equal("ulica Krucza", outcome.Match!.Street);
        Assert.Equal("50", outcome.Match.Number);
        Assert.Equal(UugMatchKind.Address, outcome.Match.Kind);
        Assert.Empty(outcome.Candidates);
        Assert.Null(outcome.Message);
        Assert.Single(stub.RequestUris);
    }

    [Fact]
    public async Task Duplicate_points_of_one_address_resolve()
    {
        // The same address twice (one record per entrance, say): one distinct address point, so it resolves; the accuracy and the point may differ.
        var first = Address();
        var second = Address(accuracy: 0.9);
        var search = new AddressSearch(new FakeUug([first, second]));

        var outcome = await ResolveAsync(search, "Warszawa, Krucza 50");

        Assert.Equal(AddressOutcomeKind.Resolved, outcome.Kind);
        Assert.Same(first, outcome.Match);
        Assert.Empty(outcome.Candidates);
    }

    [Fact]
    public async Task Matches_that_are_not_addresses_do_not_count_against_one_address()
    {
        var address = Address();
        var search = new AddressSearch(new FakeUug([Street(), address]));

        var outcome = await ResolveAsync(search, "Warszawa, Krucza 50");

        Assert.Equal(AddressOutcomeKind.Resolved, outcome.Kind);
        Assert.Same(address, outcome.Match);
    }

    [Fact]
    public async Task Two_different_addresses_give_candidates()
    {
        var matches = new[] { Address(number: "50"), Address(number: "52"), Street() };
        var search = new AddressSearch(new FakeUug(matches));

        var outcome = await ResolveAsync(search, "Warszawa, Krucza");

        Assert.Equal(AddressOutcomeKind.Candidates, outcome.Kind);
        Assert.Null(outcome.Match);
        Assert.Equal(matches, outcome.Candidates);
        Assert.False(string.IsNullOrWhiteSpace(outcome.Message));
    }

    [Fact]
    public async Task The_candidates_have_no_repeated_rows_and_keep_the_geocoders_order()
    {
        // One record per entrance: Krucza 50 comes twice (the accuracy and the point differ), the street twice, and Krucza 52 once.
        var street = Street();
        var first = Address(number: "50");
        var second = Address(number: "50", accuracy: 0.9, at: new Wgs84Point(52.2290, 21.0180));
        var fiftyTwo = Address(number: "52");
        var search = new AddressSearch(new FakeUug([street, first, second, fiftyTwo, Street()]));

        var outcome = await ResolveAsync(search, "Warszawa, Krucza");

        Assert.Equal(AddressOutcomeKind.Candidates, outcome.Kind);
        Assert.Null(outcome.Match);
        Assert.Equal(3, outcome.Candidates.Count);
        Assert.Same(street, outcome.Candidates[0]); // the first of each is kept, in the order the geocoder gave
        Assert.Same(first, outcome.Candidates[1]);
        Assert.Same(fiftyTwo, outcome.Candidates[2]);
        Assert.False(string.IsNullOrWhiteSpace(outcome.Message));
    }

    [Fact]
    public async Task Repeats_of_a_street_alone_are_one_candidate()
    {
        var search = new AddressSearch(new FakeUug([Street(), Street()]));

        var outcome = await ResolveAsync(search, "Warszawa, Krucza");

        Assert.Equal(AddressOutcomeKind.Candidates, outcome.Kind);
        Assert.Single(outcome.Candidates);
    }

    [Fact]
    public async Task A_street_and_a_bare_address_with_the_same_codes_are_two_candidates()
    {
        // The kind is part of the key: an address record that carries no number and no postal code is not the street's own row.
        var street = Street();
        var bare = new UugMatch { Kind = UugMatchKind.Address, City = "Warszawa", Street = "ulica Krucza", Teryt = "146501", Simc = "0918123", Ulic = "10013", Location = Krucza };
        var search = new AddressSearch(new FakeUug([street, bare, Address(number: "52")]));

        var outcome = await ResolveAsync(search, "Warszawa, Krucza");

        Assert.Equal(AddressOutcomeKind.Candidates, outcome.Kind);
        Assert.Equal(3, outcome.Candidates.Count);
    }

    [Fact]
    public async Task Addresses_that_differ_only_in_the_street_code_are_two_points()
    {
        var search = new AddressSearch(new FakeUug([Address(ulic: "10013"), Address(ulic: "10014")]));

        var outcome = await ResolveAsync(search, "Warszawa, Krucza 50");

        Assert.Equal(AddressOutcomeKind.Candidates, outcome.Kind);
        Assert.Equal(2, outcome.Candidates.Count);
    }

    [Fact]
    public async Task A_street_without_a_number_gives_candidates()
    {
        var (search, stub) = FromFixture("street-krucza.json");

        var outcome = await ResolveAsync(search, "Warszawa, Krucza");

        Assert.Equal(AddressOutcomeKind.Candidates, outcome.Kind);
        Assert.Null(outcome.Match);
        var candidate = Assert.Single(outcome.Candidates);
        Assert.Equal(UugMatchKind.Street, candidate.Kind);
        Assert.False(string.IsNullOrWhiteSpace(outcome.Message));
        Assert.Single(stub.RequestUris);
    }

    [Fact]
    public async Task Nothing_found_is_None()
    {
        var (search, stub) = FromFixture("none.json");

        var outcome = await ResolveAsync(search, "Nowhere 1");

        Assert.Equal(AddressOutcomeKind.None, outcome.Kind);
        Assert.Null(outcome.Match);
        Assert.Empty(outcome.Candidates);
        Assert.Equal(NothingFound, outcome.Message);
        Assert.Single(stub.RequestUris);
    }

    // The shape rule of IdentifierTools.LooksLikeKw (src/PolishOpenData.Mcp): court code, 8 digits, a check digit; spaces, case and '/' or '-' do not matter; the check digit is not verified.
    [Theory]
    [InlineData("WL1A/00272852/9")]
    [InlineData("wl1a/00272852/9")]
    [InlineData("WL1A 00272852 9")]
    [InlineData(" WL1A / 00272852 / 9 ")]
    [InlineData("WL1A-00272852-9")]
    [InlineData("WL1A002728529")]
    [InlineData("WL1A/00272852-9")]
    [InlineData("WL1A/00272852/0")] // wrong check digit: still refused
    [InlineData("WA1M/00012345/1")]
    [InlineData("wa1m 00012345 9")]
    [InlineData("1234567890123")] // shape only: four letters or digits, eight digits, one digit
    public async Task A_KW_number_is_refused_without_a_request(string text)
    {
        var uug = new FakeUug([Address()]);

        var outcome = await ResolveAsync(new AddressSearch(uug), text);

        Assert.Equal(AddressOutcomeKind.Refused, outcome.Kind);
        Assert.Equal(KwRefusal, outcome.Message);
        Assert.Null(outcome.Match);
        Assert.Empty(outcome.Candidates);
        Assert.Empty(uug.Texts);
    }

    [Fact]
    public async Task The_refusal_never_echoes_the_text()
    {
        var outcome = await ResolveAsync(new AddressSearch(new FakeUug([])), "WL1A/00272852/9");

        Assert.DoesNotContain("00272852", outcome.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("WL1A", outcome.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("00-025")] // a postal code
    [InlineData("Warszawa, Krucza 50")]
    [InlineData("12345678")] // eight digits
    [InlineData("WL1A/00272852")] // no check digit
    [InlineData("WL1A/00272852/90")] // two check digits
    [InlineData("WL1A/0027285/9")] // seven digits
    [InlineData("WL1A\t00272852\t9")] // only spaces are removed, as in IdentifierTools
    [InlineData("WL1A/00272852/9 Warszawa")]
    [InlineData("ulica Krucza 50 m. 12, Warszawa")]
    [InlineData("Krucza 50 WA1M/00012345/6")] // typed input keeps the anchored rule of the MCP tool; only a link's label is checked for an embedded number (KwShape.Contains)
    public async Task Text_that_only_looks_like_a_KW_is_looked_up(string text)
    {
        var uug = new FakeUug([]);

        var outcome = await ResolveAsync(new AddressSearch(uug), text);

        Assert.NotEqual(AddressOutcomeKind.Refused, outcome.Kind);
        Assert.Equal(text, Assert.Single(uug.Texts));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" \t \r\n")]
    public async Task Empty_text_makes_no_request(string text)
    {
        var uug = new FakeUug([Address()]);

        var outcome = await ResolveAsync(new AddressSearch(uug), text);

        Assert.Equal(AddressOutcomeKind.None, outcome.Kind);
        Assert.Null(outcome.Match);
        Assert.Empty(outcome.Candidates);
        Assert.Empty(uug.Texts);
    }

    private sealed class FakeUug(IReadOnlyList<UugMatch> matches) : IUugClient
    {
        public List<string> Texts { get; } = [];

        public Task<IReadOnlyList<UugMatch>> GeocodeAsync(string address, CancellationToken cancellationToken = default)
        {
            Texts.Add(address);
            return Task.FromResult(matches);
        }

        public Task<UugMatch?> ReverseGeocodeAsync(Wgs84Point point, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

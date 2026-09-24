using System;
using System.Linq;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using PolishOpenData.Mcp;

namespace PolishOpenData.Mcp.Tests;

public class IdentifierToolsTests
{
    private static JsonElement Results(CallToolResult result)
    {
        Assert.NotEqual(true, result.IsError);
        using var document = JsonDocument.Parse(((TextContentBlock)result.Content[0]).Text);
        return document.RootElement.GetProperty("results").Clone();
    }

    private static string[] Strings(JsonElement element, string property) =>
        element.TryGetProperty(property, out var array) ? array.EnumerateArray().Select(e => e.GetString()!).ToArray() : [];

    [Fact]
    public void Valid_kw_number_names_the_court()
    {
        var result = Results(IdentifierTools.ValidateIdentifier("WL1A/00272852/9")).EnumerateArray().Single();
        Assert.Equal("kw", result.GetProperty("kind").GetString());
        Assert.True(result.GetProperty("isValid").GetBoolean());
        Assert.Equal("WL1A/00272852/9", result.GetProperty("normalized").GetString());
        Assert.Contains(Strings(result, "details"), d => d.Contains("Aleksandrowie Kujawskim", StringComparison.Ordinal));
    }

    [Fact]
    public void Kw_typo_gets_a_suggestion_not_a_new_check_digit()
    {
        var result = Results(IdentifierTools.ValidateIdentifier("0S1U/00016474/4")).EnumerateArray().Single();
        Assert.False(result.GetProperty("isValid").GetBoolean());
        Assert.Equal(new[] { "OS1U/00016474/4" }, Strings(result, "suggestions"));
        Assert.DoesNotContain(Strings(result, "details"), d => d.Contains("check digit should be", StringComparison.Ordinal));
    }

    [Fact]
    public void Kw_with_wrong_check_digit_reports_the_expected_one()
    {
        var result = Results(IdentifierTools.ValidateIdentifier("KR1I/00012800/2")).EnumerateArray().Single();
        Assert.False(result.GetProperty("isValid").GetBoolean());
        Assert.Contains(Strings(result, "details"), d => d.Contains("check digit should be 3", StringComparison.Ordinal));
    }

    [Fact]
    public void Historical_and_unknown_court_codes()
    {
        var historical = Results(IdentifierTools.ValidateIdentifier("KR2I/00012345/2")).EnumerateArray().Single();
        Assert.True(historical.GetProperty("isValid").GetBoolean());
        Assert.Contains(Strings(historical, "details"), d => d.StartsWith("Historical court code", StringComparison.Ordinal));

        var unknown = Results(IdentifierTools.ValidateIdentifier("AA1A/00000001/9")).EnumerateArray().Single();
        Assert.True(unknown.GetProperty("isValid").GetBoolean());
        Assert.Contains(Strings(unknown, "warnings"), w => w.Contains("not in the current or historical", StringComparison.Ordinal));
    }

    [Fact]
    public void Ten_digits_are_checked_as_nip_and_krs()
    {
        var results = Results(IdentifierTools.ValidateIdentifier("7740001454")).EnumerateArray().ToArray();
        Assert.Equal(new[] { "nip", "krs" }, results.Select(r => r.GetProperty("kind").GetString()));
        Assert.All(results, r => Assert.True(r.GetProperty("isValid").GetBoolean()));
    }

    [Fact]
    public void Regon_and_krs_padded_regon()
    {
        var valid = Results(IdentifierTools.ValidateIdentifier("610188201")).EnumerateArray().Single();
        Assert.Equal("regon", valid.GetProperty("kind").GetString());
        Assert.True(valid.GetProperty("isValid").GetBoolean());

        var padded = Results(IdentifierTools.ValidateIdentifier("61018820100000")).EnumerateArray().Single();
        Assert.False(padded.GetProperty("isValid").GetBoolean());
        Assert.Equal("610188201", padded.GetProperty("normalized").GetString());
        Assert.Contains(Strings(padded, "warnings"), w => w.Contains("9 digits + 00000", StringComparison.Ordinal));

        var paddedButValid = Results(IdentifierTools.ValidateIdentifier("10000004300000")).EnumerateArray().Single();
        Assert.True(paddedButValid.GetProperty("isValid").GetBoolean());
        Assert.Contains(Strings(paddedButValid, "warnings"), w => w.Contains("100000043", StringComparison.Ordinal));
    }

    [Fact]
    public void Iban_form_is_detected_as_nrb()
    {
        var result = Results(IdentifierTools.ValidateIdentifier("PL06160011271843983820000034")).EnumerateArray().Single();
        Assert.Equal("nrb", result.GetProperty("kind").GetString());
        Assert.True(result.GetProperty("isValid").GetBoolean());
    }

    [Fact]
    public void Explicit_kind_is_case_insensitive()
    {
        var result = Results(IdentifierTools.ValidateIdentifier("7740001455", "NIP")).EnumerateArray().Single();
        Assert.Equal("nip", result.GetProperty("kind").GetString());
        Assert.False(result.GetProperty("isValid").GetBoolean());
    }

    [Fact]
    public void Unknown_kind_is_an_error_with_the_allowed_values()
    {
        var result = IdentifierTools.ValidateIdentifier("7740001454", "pesel");
        Assert.True(result.IsError == true);
        Assert.Contains("nip, regon, krs, nrb, kw", ((TextContentBlock)result.Content[0]).Text, StringComparison.Ordinal);
    }
}

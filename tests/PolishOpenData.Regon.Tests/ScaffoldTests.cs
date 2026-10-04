using PolishOpenData.Internal;

namespace PolishOpenData.Bir.Tests;

public class ScaffoldTests
{
    // Valid-checksum identifiers that the fixtures and the later tests rely on (checked with the Core algorithms).
    [Fact]
    public void Core_identifiers_used_by_the_fixtures_are_valid()
    {
        string[] regons =
        [
            "000331501", "999999990", "123456785", "610188201", "545772924", "142396858", "010418973", "651536363", "002003735", "021625560",
            "14726216300027", "99999999000026",
        ];
        foreach (var text in regons)
        {
            Assert.True(Regon.TryParse(text, out var regon), "REGON " + text);
            Assert.Equal(text, regon.ToString());
        }

        Assert.Equal(Regon.Parse("147262163"), Regon.Parse("14726216300027").BaseRegon);
        Assert.Equal(Regon.Parse("999999990"), Regon.Parse("99999999000026").BaseRegon);

        string[] nips = ["5261040828", "9999999999", "1234563218", "7740001454", "5260251049", "0000000000"];
        foreach (var text in nips)
        {
            Assert.True(Nip.TryParse(text, out var nip), "NIP " + text);
            Assert.Equal(text, nip.ToString());
        }

        string[] krsNumbers = ["0000028860", "0001268296"];
        foreach (var text in krsNumbers)
        {
            Assert.True(KrsNumber.TryParse(text, out var krs), "KRS " + text);
            Assert.Equal(text, krs.ToString());
        }
    }

    // Proves the project reference, the linked src/Shared sources and InternalsVisibleTo in one assertion.
    [Fact]
    public void Scaffold_sees_the_internal_shared_sources()
    {
        Assert.StartsWith("PolishOpenData/", UserAgent.Value, System.StringComparison.Ordinal);
    }
}

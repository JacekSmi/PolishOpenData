using System.Linq;

namespace PolishOpenData.Rcn;

/// <summary>The address of a transaction as RCN writes it (<c>MSC:&lt;town&gt;;UL:&lt;street&gt;;NR_PORZ:&lt;number&gt;</c>); any part can be missing.</summary>
/// <param name="Town">The town (<c>MSC</c>).</param>
/// <param name="Street">The street with its prefix, e.g. <c>ulica Krucza</c> (<c>UL</c>).</param>
/// <param name="Number">The building number (<c>NR_PORZ</c>).</param>
public sealed record RcnAddress(string? Town, string? Street, string? Number)
{
    /// <summary><c>&lt;Street&gt; &lt;Number&gt;, &lt;Town&gt;</c>, leaving out the parts that are missing.</summary>
    public override string ToString()
    {
        var street = string.Join(" ", new[] { Street, Number }.Where(p => !string.IsNullOrEmpty(p)));
        return string.Join(", ", new[] { street, Town }.Where(p => !string.IsNullOrEmpty(p)));
    }
}

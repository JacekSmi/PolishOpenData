using System.Collections.Generic;

namespace PolishOpenData.Rcn;

/// <summary>The answer to an RCN query, newest transaction first.</summary>
/// <param name="Items">At most <see cref="RcnQuery.MaxRecords"/> rows.</param>
/// <param name="Truncated"><see langword="true"/> when the service holds more rows than <see cref="RcnQuery.MaxRecords"/>; the rest were not returned.</param>
public sealed record RcnTransactions(IReadOnlyList<RcnTransaction> Items, bool Truncated);

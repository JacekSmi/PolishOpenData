using System;

namespace PolishOpenData.BialaLista.Offline;

/// <summary>The dates for which the Ministry of Finance serves a flat file: the last 30 days and tomorrow.</summary>
internal static class OfflineDateWindow
{
    /// <summary>Throws unless <c>today - 30 &lt;= date &lt;= today + 1</c>; <paramref name="today"/> is the Warsaw date.</summary>
    /// <exception cref="VatWhitelistDataUnavailableException">The date is outside the window.</exception>
    public static void Check(DateOnly date, DateOnly today)
    {
        if (date < today.AddDays(-30) || date > today.AddDays(1))
        {
            throw new VatWhitelistDataUnavailableException(
                $"No flat file is available for {date:yyyy-MM-dd}: only the last 30 days and tomorrow can be used (today in Warsaw is {today:yyyy-MM-dd}).",
                date,
                VatWhitelistDataUnavailableReason.OutsideWindow);
        }
    }
}

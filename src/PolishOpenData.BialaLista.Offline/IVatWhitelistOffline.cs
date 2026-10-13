using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PolishOpenData.BialaLista.Offline;

/// <summary>
/// Checks pairs of NIP and bank account against the Ministry of Finance's daily flat file (plik płaski), which is
/// downloaded and indexed once per date and then queried locally: no request limit, and no NIP or account leaves the machine.
/// Unofficial; not affiliated with the Polish government.
/// </summary>
/// <remarks>
/// The answers are <see cref="VatWhitelistOfflineStatus.Active"/>, <see cref="VatWhitelistOfflineStatus.Exempt"/> and
/// <see cref="VatWhitelistOfflineStatus.NotFound"/> (not found in the file). A date is answered only with the file of that
/// date; allowed dates run from 30 days ago to tomorrow (Warsaw time), otherwise <see cref="VatWhitelistDataUnavailableException"/>
/// is thrown before anything is downloaded. The first call for a date downloads the file (about 250 MB; it can take minutes
/// on a slow server) and builds the index; use <see cref="PrepareAsync"/> to do that ahead of time.
/// </remarks>
public interface IVatWhitelistOffline
{
    /// <summary>Checks one pair, preparing the date's data first if needed.</summary>
    /// <param name="nip">The taxpayer's NIP.</param>
    /// <param name="account">The bank account.</param>
    /// <param name="date">The date to check; today in Warsaw when <c>null</c>.</param>
    /// <param name="cancellationToken">Cancels the call, including a download it started.</param>
    /// <exception cref="ArgumentException"><paramref name="nip"/> or <paramref name="account"/> is the default (empty) value.</exception>
    /// <exception cref="VatWhitelistDataUnavailableException">The date is outside the allowed range, or its file cannot be used.</exception>
    Task<VatWhitelistOfflineCheck> CheckAsync(Nip nip, Nrb account, DateOnly? date = null, CancellationToken cancellationToken = default);

    /// <summary>Checks many pairs against one date's file; the results are in the order of <paramref name="pairs"/>, duplicates included.</summary>
    /// <param name="pairs">The pairs to check; an empty sequence returns an empty list without preparing any data.</param>
    /// <param name="date">The date to check; today in Warsaw when <c>null</c>.</param>
    /// <param name="cancellationToken">Cancels the call, including a download it started.</param>
    /// <exception cref="ArgumentException">A NIP or account is the default (empty) value.</exception>
    /// <exception cref="VatWhitelistDataUnavailableException">The date is outside the allowed range, or its file cannot be used.</exception>
    Task<IReadOnlyList<VatWhitelistOfflineCheck>> CheckManyAsync(IEnumerable<(Nip Nip, Nrb Account)> pairs, DateOnly? date = null, CancellationToken cancellationToken = default);

    /// <summary>Downloads and indexes the file of a date if that has not been done, and returns what it contains.</summary>
    /// <param name="date">The date to prepare; today in Warsaw when <c>null</c>.</param>
    /// <param name="progress">
    /// Receives download and indexing progress, but only when this call itself starts the preparation: a call that joins a
    /// preparation already running, or finds the date ready, reports nothing.
    /// </param>
    /// <param name="cancellationToken">Cancels the call, including a download it started.</param>
    /// <exception cref="VatWhitelistDataUnavailableException">The date is outside the allowed range, or its file cannot be used.</exception>
    Task<VatWhitelistFileInfo> PrepareAsync(DateOnly? date = null, IProgress<VatWhitelistPreparationProgress>? progress = null, CancellationToken cancellationToken = default);
}

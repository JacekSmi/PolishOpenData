using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PolishOpenData.Internal;

namespace PolishOpenData.BialaLista.Offline;

/// <summary>
/// The prepared indexes of one data directory, shared by every caller in the process; other processes are kept out by a
/// lock file per date. Files: <c>{yyyyMMdd}.wlidx</c> (index), <c>.wlidx.tmp-{guid}</c> (being written), <c>.7z.part</c>
/// (download), <c>.lock</c> (exclusive lock, kept empty until the date leaves the window), <c>.used</c> (its last-write time
/// is the last use).
/// </summary>
internal sealed class OfflineIndexStore : IDisposable
{
    private const int MaxRestarts = 3;
    private static readonly TimeSpan LockPollInterval = TimeSpan.FromMilliseconds(200);

    private readonly string _dir;
    private readonly int _datesToKeep;
    private readonly TimeSpan _lockTimeout;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<DateOnly, Lazy<Task<WhitelistIndex>>> _preparations = new();
    private readonly List<WhitelistIndex> _opened = new();
    private bool _disposed;

    public OfflineIndexStore(string dataDirectory, int datesToKeep, TimeSpan lockTimeout, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(dataDirectory);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentOutOfRangeException.ThrowIfLessThan(datesToKeep, 1);
        _dir = dataDirectory;
        _datesToKeep = datesToKeep;
        _lockTimeout = lockTimeout;
        _time = time;
    }

    /// <summary>
    /// Returns the open index of <paramref name="date"/>, preparing it first if needed (<paramref name="download"/> saves the
    /// archive to the path it is given); concurrent callers for one date share one preparation.
    /// </summary>
    /// <exception cref="TimeoutException">Another process held the date's lock longer than the lock timeout.</exception>
    /// <exception cref="IOException">The data directory cannot be created, read or written (the message names it).</exception>
    public async Task<WhitelistIndex> GetAsync(
        DateOnly date,
        Func<string, IProgress<VatWhitelistPreparationProgress>?, CancellationToken, Task> download,
        IProgress<VatWhitelistPreparationProgress>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(download);
        for (var restarts = 0; ; restarts++)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            // The preparation runs under the token (and progress) of the caller that started it; a failure removes it so the next call retries.
            Lazy<Task<WhitelistIndex>>? mine = null;
            mine = new(() => Task.Run(() => RunAsync(date, mine!, download, progress, ct)));
            var preparation = _preparations.GetOrAdd(date, mine);
            var task = preparation.Value;
            try
            {
                // The starter's own token cancels the preparation, so it awaits it (and its clean-up of partial files);
                // a caller that joined stops waiting as soon as its own token fires.
                return await (preparation == mine ? task : task.WaitAsync(ct)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (task.IsCanceled && !ct.IsCancellationRequested && restarts < MaxRestarts)
            {
                // the caller that started the shared preparation cancelled it; this caller did not, so it prepares again
            }
        }
    }

    public void Dispose()
    {
        lock (_opened)
        {
            _disposed = true;
            foreach (var index in _opened)
            {
                index.Dispose();
            }
            _opened.Clear();
        }
    }

    private async Task<WhitelistIndex> RunAsync(
        DateOnly date,
        Lazy<Task<WhitelistIndex>> self,
        Func<string, IProgress<VatWhitelistPreparationProgress>?, CancellationToken, Task> download,
        IProgress<VatWhitelistPreparationProgress>? progress,
        CancellationToken ct)
    {
        try
        {
            var index = await PrepareAsync(date, download, progress, ct).ConfigureAwait(false);
            lock (_opened)
            {
                if (_disposed)
                {
                    index.Dispose();
                    throw new ObjectDisposedException(nameof(OfflineIndexStore));
                }
                _opened.Add(index);
            }
            return index;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Forget(date, self);
            throw new IOException($"The data directory '{_dir}' cannot be used: {ex.Message}", ex);
        }
        catch
        {
            Forget(date, self);
            throw;
        }
    }

    private async Task<WhitelistIndex> PrepareAsync(
        DateOnly date,
        Func<string, IProgress<VatWhitelistPreparationProgress>?, CancellationToken, Task> download,
        IProgress<VatWhitelistPreparationProgress>? progress,
        CancellationToken ct)
    {
        _ = Directory.CreateDirectory(_dir);
        var name = Name(date);
        var indexPath = PathOf(name + ".wlidx");
        WhitelistIndex index;
        var built = false;
        using (await AcquireLockAsync(PathOf(name + ".lock"), ct).ConfigureAwait(false))
        {
            try
            {
                // another process may have built it while this one waited for the lock
                index = Open(indexPath, date);
            }
            catch (FileNotFoundException)
            {
                index = await BuildAsync(date, name, download, progress, ct).ConfigureAwait(false);
                built = true;
            }
            catch (VatWhitelistDataUnavailableException ex) when (ex.Reason == VatWhitelistDataUnavailableReason.UnexpectedFormat)
            {
                File.Delete(indexPath);
                index = await BuildAsync(date, name, download, progress, ct).ConfigureAwait(false);
                built = true;
            }
        }

        try
        {
            var used = PathOf(name + ".used");
            using (File.Open(used, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            {
            }
            File.SetLastWriteTimeUtc(used, _time.GetUtcNow().UtcDateTime);
            if (built)
            {
                ApplyRetention(date);
            }
            return index;
        }
        catch
        {
            index.Dispose();
            throw;
        }
    }

    private async Task<WhitelistIndex> BuildAsync(
        DateOnly date,
        string name,
        Func<string, IProgress<VatWhitelistPreparationProgress>?, CancellationToken, Task> download,
        IProgress<VatWhitelistPreparationProgress>? progress,
        CancellationToken ct)
    {
        var part = PathOf(name + ".7z.part");
        var temp = PathOf(name + ".wlidx.tmp-" + Guid.NewGuid().ToString("N"));
        var indexPath = PathOf(name + ".wlidx");
        try
        {
            await download(part, progress, ct).ConfigureAwait(false);
            ParsedFlatFile parsed;
            using (var archive = new FileStream(part, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                progress?.Report(new VatWhitelistPreparationProgress(VatWhitelistPreparationStage.Indexing, archive.Length, archive.Length));
                parsed = FlatFileArchive.Read(archive, date, ct);
            }
            WhitelistIndex.Write(temp, date, parsed);
            File.Move(temp, indexPath);
        }
        finally
        {
            TryDelete(part);
            TryDelete(temp);
        }
        return Open(indexPath, date);
    }

    private async Task<FileStream> AcquireLockAsync(string path, CancellationToken ct)
    {
        var waited = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            // A sharing violation is a plain IOException on Windows and Unix; its subclasses (not found, path too long) are real errors.
            // shortcut: any other plain IOException is also retried until the timeout; check HResult if that ever misleads.
            catch (IOException ex) when (ex.GetType() == typeof(IOException))
            {
                var left = _lockTimeout - waited.Elapsed;
                if (left <= TimeSpan.Zero)
                {
                    throw new TimeoutException($"Another process held the lock file '{path}' for longer than {_lockTimeout}.", ex);
                }
                await Task.Delay(left < LockPollInterval ? left : LockPollInterval, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Deletes every file of dates outside MF's window, then the indexes beyond the <c>datesToKeep</c> most recently used; never <paramref name="built"/>.</summary>
    private void ApplyRetention(DateOnly built)
    {
        var today = WarsawTime.Today(_time);
        var indexes = new List<(DateOnly Date, DateTime Used)>();
        foreach (var path in Directory.GetFiles(_dir))
        {
            var fileName = Path.GetFileName(path);
            if (fileName.Length < 9 || fileName[8] != '.'
                || !DateOnly.TryParseExact(fileName.Substring(0, 8), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                || date == built)
            {
                continue;
            }

            if (date < today.AddDays(-30) || date > today.AddDays(1))
            {
                TryDelete(path);
            }
            else if (fileName.Length == 14 && fileName.EndsWith(".wlidx", StringComparison.Ordinal))
            {
                // a missing marker reads as 1601-01-01, i.e. least recently used
                indexes.Add((date, File.GetLastWriteTimeUtc(PathOf(Name(date) + ".used"))));
            }
        }

        foreach (var (date, _) in indexes.OrderByDescending(i => i.Used).Skip(_datesToKeep - 1))
        {
            TryDelete(PathOf(Name(date) + ".wlidx"));
            TryDelete(PathOf(Name(date) + ".used"));
        }
    }

    private static WhitelistIndex Open(string path, DateOnly date)
    {
        try
        {
            return WhitelistIndex.Open(path);
        }
        catch (VatWhitelistDataUnavailableException ex) when (ex.Date == default)
        {
            throw new VatWhitelistDataUnavailableException(ex.Message, date, ex.Reason, ex);
        }
    }

    private static string Name(DateOnly date) => date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    private string PathOf(string fileName) => Path.Combine(_dir, fileName);

    // Removes only this preparation: a newer one for the same date may already be registered.
    private void Forget(DateOnly date, Lazy<Task<WhitelistIndex>> preparation) =>
        ((ICollection<KeyValuePair<DateOnly, Lazy<Task<WhitelistIndex>>>>)_preparations).Remove(new(date, preparation));

    // Failures are ignored: the file is retried at the next build.
    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

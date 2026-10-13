using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.BialaLista.Offline.Tests;

public sealed class OfflineIndexStoreTests : IDisposable
{
    private const string ActiveNip = "5261040828";
    private const string ActiveAccount = "06160011271843983820000034";

    private static readonly DateOnly Date = new(2026, 10, 13);

    private readonly string _root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    private readonly string _dir;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 13, 8, 0, 0, TimeSpan.Zero));
    private int _downloads;

    public OfflineIndexStoreTests() => _dir = Path.Combine(_root, "data");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
    }

    private OfflineIndexStore Store(int datesToKeep = 3, TimeSpan? lockTimeout = null, string? dir = null) =>
        new(dir ?? _dir, datesToKeep, lockTimeout ?? TimeSpan.FromSeconds(10), _time);

    private Task Download(string archivePath, IProgress<VatWhitelistPreparationProgress>? progress, CancellationToken ct)
    {
        Interlocked.Increment(ref _downloads);
        File.WriteAllBytes(archivePath, Fixture.ReadBytes("bialalista-offline/20261013.7z"));
        return Task.CompletedTask;
    }

    private string PathOf(string name) => Path.Combine(_dir, name);

    private string[] FileNames() => Directory.GetFiles(_dir).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray()!;

    private void PlaceIndex(string name, DateTimeOffset? used = null)
    {
        Directory.CreateDirectory(_dir);
        using (var json = new MemoryStream(Fixture.ReadBytes("bialalista-offline/20261013.json")))
        {
            WhitelistIndex.Write(PathOf(name + ".wlidx"), Date, FlatFileParser.Parse(json, Date, CancellationToken.None));
        }
        if (used is { } at)
        {
            File.WriteAllBytes(PathOf(name + ".used"), []);
            File.SetLastWriteTimeUtc(PathOf(name + ".used"), at.UtcDateTime);
        }
    }

    private FileStream HoldLock(string name = "20261013")
    {
        Directory.CreateDirectory(_dir);
        return new FileStream(PathOf(name + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private void AssertNoPreparationLeftovers()
    {
        var names = FileNames();
        Assert.DoesNotContain(names, n => n.EndsWith(".part", StringComparison.Ordinal));
        Assert.DoesNotContain(names, n => n.Contains(".tmp-"));
        Assert.DoesNotContain(names, n => n.EndsWith(".wlidx", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Builds_index_and_touches_used_marker()
    {
        using var store = Store();
        var index = await store.GetAsync(Date, Download, null, Token);

        Assert.Equal(VatWhitelistOfflineStatus.Active, index.Lookup(ActiveNip, ActiveAccount).Status);
        Assert.Equal(new[] { "20261013.lock", "20261013.used", "20261013.wlidx" }, FileNames());
        Assert.Equal(_time.GetUtcNow().UtcDateTime, File.GetLastWriteTimeUtc(PathOf("20261013.used")));
        Assert.Same(index, await store.GetAsync(Date, Download, null, Token));
        Assert.Equal(1, _downloads);
    }

    [Fact]
    public async Task Concurrent_requests_same_date_download_once()
    {
        using var store = Store();
        async Task SlowDownload(string path, IProgress<VatWhitelistPreparationProgress>? progress, CancellationToken ct)
        {
            await Task.Delay(100, ct);
            await Download(path, progress, ct);
        }

        var indexes = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() => store.GetAsync(Date, SlowDownload, null, Token), Token)));

        Assert.Equal(1, _downloads);
        Assert.All(indexes, i => Assert.Same(indexes[0], i));
    }

    [Fact]
    public async Task Second_store_waits_for_lock_then_reuses_index()
    {
        using var store = Store();
        Task<WhitelistIndex> pending;
        using (HoldLock())
        {
            pending = store.GetAsync(Date, Download, null, Token);
            await Task.Delay(500, Token);
            Assert.False(pending.IsCompleted);
            PlaceIndex("20261013");
        }

        var index = await pending;
        Assert.Equal(VatWhitelistOfflineStatus.Active, index.Lookup(ActiveNip, ActiveAccount).Status);
        Assert.Equal(0, _downloads);
    }

    [Fact]
    public async Task Lock_timeout_throws_with_lock_path()
    {
        using var store = Store(lockTimeout: TimeSpan.FromMilliseconds(300));
        using var held = HoldLock();

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => store.GetAsync(Date, Download, null, Token));
        Assert.Contains(PathOf("20261013.lock"), exception.Message);
        Assert.Equal(0, _downloads);
    }

    [Fact]
    public async Task Failed_download_leaves_no_index_and_next_call_rebuilds()
    {
        using var store = Store();
        Task Failing(string path, IProgress<VatWhitelistPreparationProgress>? progress, CancellationToken ct)
        {
            Interlocked.Increment(ref _downloads);
            File.WriteAllBytes(path, [1, 2, 3]);
            throw new VatWhitelistDataUnavailableException("boom", Date, VatWhitelistDataUnavailableReason.DownloadFailed);
        }

        var exception = await Assert.ThrowsAsync<VatWhitelistDataUnavailableException>(() => store.GetAsync(Date, Failing, null, Token));
        Assert.Equal(VatWhitelistDataUnavailableReason.DownloadFailed, exception.Reason);
        AssertNoPreparationLeftovers();

        var index = await store.GetAsync(Date, Download, null, Token);
        Assert.Equal(VatWhitelistOfflineStatus.Active, index.Lookup(ActiveNip, ActiveAccount).Status);
        Assert.Equal(2, _downloads);
    }

    [Fact]
    public async Task Corrupt_archive_leaves_no_files()
    {
        using var store = Store();
        Task Garbage(string path, IProgress<VatWhitelistPreparationProgress>? progress, CancellationToken ct)
        {
            File.WriteAllBytes(path, [1, 2, 3]);
            return Task.CompletedTask;
        }

        var exception = await Assert.ThrowsAsync<VatWhitelistDataUnavailableException>(() => store.GetAsync(Date, Garbage, null, Token));
        Assert.Equal(VatWhitelistDataUnavailableReason.UnexpectedFormat, exception.Reason);
        AssertNoPreparationLeftovers();
    }

    [Fact]
    public async Task Cancelled_preparation_leaves_no_files_and_next_call_rebuilds()
    {
        using var store = Store();
        using var cts = new CancellationTokenSource();
        Task Cancelling(string path, IProgress<VatWhitelistPreparationProgress>? progress, CancellationToken ct)
        {
            Interlocked.Increment(ref _downloads);
            File.WriteAllBytes(path, [1, 2, 3]);
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.GetAsync(Date, Cancelling, null, cts.Token));
        AssertNoPreparationLeftovers();
        using (var other = Store(lockTimeout: TimeSpan.Zero, dir: _dir))
        {
            // the lock is free: a zero timeout acquires it at once (and builds, which proves the next call rebuilds)
            _ = await other.GetAsync(Date, Download, null, Token);
        }
        File.Delete(PathOf("20261013.wlidx"));

        var index = await store.GetAsync(Date, Download, null, Token);
        Assert.Equal(VatWhitelistOfflineStatus.Active, index.Lookup(ActiveNip, ActiveAccount).Status);
        Assert.Equal(3, _downloads);
    }

    [Fact]
    public async Task Waiter_survives_cancellation_of_the_caller_that_started_the_preparation()
    {
        using var store = Store();
        using var first = new CancellationTokenSource();
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        async Task Blocking(string path, IProgress<VatWhitelistPreparationProgress>? progress, CancellationToken ct)
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                started.SetResult(true);
                await Task.Delay(Timeout.Infinite, ct);
            }
            await Download(path, progress, ct);
        }

        var a = store.GetAsync(Date, Blocking, null, first.Token);
        await started.Task;
        var b = store.GetAsync(Date, Blocking, null, Token);
        first.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => a);
        var index = await b;
        Assert.Equal(VatWhitelistOfflineStatus.Active, index.Lookup(ActiveNip, ActiveAccount).Status);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Corrupt_existing_index_is_rebuilt()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(PathOf("20261013.wlidx"), [1, 2, 3]);
        using var store = Store();

        var index = await store.GetAsync(Date, Download, null, Token);
        Assert.Equal(VatWhitelistOfflineStatus.Active, index.Lookup(ActiveNip, ActiveAccount).Status);
        Assert.Equal(1, _downloads);
    }

    [Fact]
    public async Task Retention_keeps_most_recently_used()
    {
        // older date used more recently: the .used time decides, not the date
        PlaceIndex("20261011", _time.GetUtcNow().AddHours(-1));
        PlaceIndex("20261012", _time.GetUtcNow().AddHours(-2));
        using var store = Store(datesToKeep: 2);

        _ = await store.GetAsync(Date, Download, null, Token);

        Assert.True(File.Exists(PathOf("20261011.wlidx")));
        Assert.False(File.Exists(PathOf("20261012.wlidx")));
        Assert.False(File.Exists(PathOf("20261012.used")));
        Assert.True(File.Exists(PathOf("20261013.wlidx")));
    }

    [Fact]
    public async Task Index_outside_window_is_deleted()
    {
        PlaceIndex("20260912", _time.GetUtcNow());
        PlaceIndex("20260913", _time.GetUtcNow());
        HoldLock("20260912").Dispose();
        using var store = Store(datesToKeep: 5);

        _ = await store.GetAsync(Date, Download, null, Token);

        Assert.Equal(new[] { "20260913.used", "20260913.wlidx", "20261013.lock", "20261013.used", "20261013.wlidx" }, FileNames());
    }

    [Fact]
    public async Task Deleted_index_still_answers_while_open()
    {
        using var store = Store();
        var index = await store.GetAsync(Date, Download, null, Token);

        File.Delete(PathOf("20261013.wlidx"));

        Assert.Equal(VatWhitelistOfflineStatus.Active, index.Lookup(ActiveNip, ActiveAccount).Status);
    }

    [Fact]
    public async Task Creates_missing_data_directory()
    {
        var dir = Path.Combine(_root, "a", "b");
        using var store = Store(dir: dir);

        _ = await store.GetAsync(Date, Download, null, Token);

        Assert.True(File.Exists(Path.Combine(dir, "20261013.wlidx")));
    }

    [Fact]
    public async Task Unwritable_directory_fails_with_path_in_message()
    {
        Directory.CreateDirectory(_root);
        var file = Path.Combine(_root, "file");
        File.WriteAllBytes(file, []);
        var dir = Path.Combine(file, "sub");
        using var store = Store(dir: dir);

        var exception = await Assert.ThrowsAsync<IOException>(() => store.GetAsync(Date, Download, null, Token));
        Assert.Contains(dir, exception.Message);
    }

    [Fact]
    public async Task Cached_index_that_left_the_window_is_closed()
    {
        using var store = Store();
        var old = await store.GetAsync(Date, Download, null, Token);
        _time.Advance(TimeSpan.FromDays(32));
        var today = Date.AddDays(32);
        PlaceIndex(today.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture));

        var current = await store.GetAsync(today, Download, null, Token);

        Assert.Throws<ObjectDisposedException>(() => old.Lookup(ActiveNip, ActiveAccount));
        Assert.Equal(VatWhitelistOfflineStatus.Active, current.Lookup(ActiveNip, ActiveAccount).Status);
        Assert.Equal(1, _downloads);
    }

    [Fact]
    public async Task Index_is_returned_when_the_used_marker_cannot_be_touched()
    {
        PlaceIndex("20261013", _time.GetUtcNow());
        File.SetAttributes(PathOf("20261013.used"), FileAttributes.ReadOnly);
        try
        {
            using var store = Store();

            var index = await store.GetAsync(Date, Download, null, Token);

            Assert.Equal(VatWhitelistOfflineStatus.Active, index.Lookup(ActiveNip, ActiveAccount).Status);
            Assert.Equal(0, _downloads);
        }
        finally
        {
            File.SetAttributes(PathOf("20261013.used"), FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task Dispose_closes_indexes_and_rejects_new_calls()
    {
        var store = Store();
        var index = await store.GetAsync(Date, Download, null, Token);

        store.Dispose();

        Assert.Throws<ObjectDisposedException>(() => index.Lookup(ActiveNip, ActiveAccount));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.GetAsync(Date, Download, null, Token));
    }
}

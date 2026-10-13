using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.BialaLista.Offline.Tests;

public sealed class FlatFileDownloaderTests : IDisposable
{
    private static readonly Uri BaseAddress = new("https://plikplaski.mf.gov.pl/pliki/");
    private static readonly DateOnly Today = new(2026, 10, 13);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    private readonly string _target;

    public FlatFileDownloaderTests()
    {
        Directory.CreateDirectory(_dir);
        _target = Path.Combine(_dir, "20261013.7z.part");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
        }
    }

    private static HttpResponseMessage Archive(byte[]? body = null, long? announced = null)
    {
        var content = new ByteArrayContent(body ?? Fixture.ReadBytes("bialalista-offline/20261013.7z"));
        if (announced is { } length)
        {
            content.Headers.ContentLength = length;
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private async Task Download(StubHttpMessageHandler handler, DateOnly? date = null, IProgress<VatWhitelistPreparationProgress>? progress = null, CancellationToken? ct = null)
    {
        using var http = new HttpClient(handler);
        await FlatFileDownloader.DownloadAsync(http, BaseAddress, date ?? Today, Today, _target, progress, ct ?? TestContext.Current.CancellationToken);
    }

    private sealed class Collector : IProgress<VatWhitelistPreparationProgress>
    {
        public List<VatWhitelistPreparationProgress> Reports { get; } = new();

        public void Report(VatWhitelistPreparationProgress value) => Reports.Add(value);
    }

    // a seekable stream would let StreamContent compute the Content-Length
    private sealed class UnknownLength : MemoryStream
    {
        public UnknownLength(byte[] bytes)
            : base(bytes)
        {
        }

        public override bool CanSeek => false;
    }

    // a read that never completes and ignores its token (like a stalled .NET Framework response stream); only Dispose ends it
    private sealed class Stalled : Stream
    {
        private readonly TaskCompletionSource<int> _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Reading { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Reading.TrySetResult(true);
            return _disposed.Task;
        }

#if NET
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            new(ReadAsync(Array.Empty<byte>(), 0, 0, cancellationToken));
#endif

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            _disposed.TrySetException(new ObjectDisposedException(nameof(Stalled)));
            base.Dispose(disposing);
        }
    }

    // cancels the download after the first chunk has been handed out, i.e. while the body is being written
    private sealed class CancelAfterFirstRead : MemoryStream
    {
        private readonly CancellationTokenSource _cts;

        public CancelAfterFirstRead(CancellationTokenSource cts)
            : base(new byte[1_000_000]) => _cts = cts;

#if NET
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await base.ReadAsync(buffer, cancellationToken);
            _cts.Cancel();
            return read;
        }
#else
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var read = await base.ReadAsync(buffer, offset, count, cancellationToken);
            _cts.Cancel();
            return read;
        }
#endif
    }

    [Fact]
    public async Task Requests_dated_url()
    {
        var handler = new StubHttpMessageHandler(_ => Archive());
        await Download(handler);
        Assert.Equal(new Uri("https://plikplaski.mf.gov.pl/pliki/20261013.7z"), Assert.Single(handler.RequestUris));
    }

    [Fact]
    public async Task Writes_body_and_reports_progress()
    {
        var progress = new Collector();
        await Download(new StubHttpMessageHandler(_ => Archive()), progress: progress);
        var expected = Fixture.ReadBytes("bialalista-offline/20261013.7z");
        Assert.Equal(expected, File.ReadAllBytes(_target));
        var last = progress.Reports.Last();
        Assert.Equal(VatWhitelistPreparationStage.Downloading, last.Stage);
        Assert.Equal(expected.Length, last.TotalBytes);
        Assert.Equal(last.TotalBytes, last.BytesReceived);
    }

    [Fact]
    public async Task Unknown_length_reports_null_total()
    {
        var progress = new Collector();
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new UnknownLength(new byte[] { 1, 2, 3 })) });
        await Download(handler, progress: progress);
        Assert.Null(progress.Reports.Last().TotalBytes);
        Assert.Equal(3, progress.Reports.Last().BytesReceived);
    }

    [Fact]
    public async Task NotFound_for_today_is_not_published_yet()
    {
        var exception = await Assert.ThrowsAsync<VatWhitelistDataUnavailableException>(
            () => Download(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Empty(HttpStatusCode.NotFound))));
        Assert.Equal(VatWhitelistDataUnavailableReason.NotPublishedYet, exception.Reason);
        Assert.Equal(Today, exception.Date);
        Assert.False(File.Exists(_target));
    }

    [Fact]
    public async Task NotFound_for_past_date_is_outside_window()
    {
        var date = Today.AddDays(-5);
        var exception = await Assert.ThrowsAsync<VatWhitelistDataUnavailableException>(
            () => Download(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Empty(HttpStatusCode.NotFound)), date));
        Assert.Equal(VatWhitelistDataUnavailableReason.OutsideWindow, exception.Reason);
        Assert.Equal(date, exception.Date);
        // a missing holiday file or a transient 404 inside the window must not be blamed on MF's retention
        Assert.Equal("MF does not serve the flat file of 2026-10-08 (HTTP 404).", exception.Message);
    }

    [Fact]
    public async Task Server_error_is_http_request_exception_and_no_file_left()
    {
        File.WriteAllBytes(_target, new byte[] { 1 });
        await Assert.ThrowsAsync<HttpRequestException>(
            () => Download(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Empty(HttpStatusCode.InternalServerError))));
        Assert.False(File.Exists(_target));
    }

    [Fact]
    public async Task Truncated_body_is_download_failed_and_no_file_left()
    {
        var body = Fixture.ReadBytes("bialalista-offline/20261013.7z");
        var exception = await Assert.ThrowsAsync<VatWhitelistDataUnavailableException>(
            () => Download(new StubHttpMessageHandler(_ => Archive(body.Take(100).ToArray(), body.Length))));
        Assert.Equal(VatWhitelistDataUnavailableReason.DownloadFailed, exception.Reason);
        Assert.Equal(Today, exception.Date);
        Assert.False(File.Exists(_target));
    }

    [Fact]
    public async Task Local_file_error_is_not_download_failed()
    {
        using var http = new HttpClient(new StubHttpMessageHandler(_ => Archive()));
        var target = Path.Combine(_dir, "missing", "20261013.7z.part");

        var exception = await Assert.ThrowsAnyAsync<IOException>(
            () => FlatFileDownloader.DownloadAsync(http, BaseAddress, Today, Today, target, null, TestContext.Current.CancellationToken));
        Assert.Contains(target, exception.Message);
    }

    [Fact]
    public async Task Cancelled_download_leaves_no_file()
    {
        using var cts = new CancellationTokenSource();
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new CancelAfterFirstRead(cts)) });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Download(handler, ct: cts.Token));
        Assert.False(File.Exists(_target));
    }

    [Fact]
    public async Task Cancelling_a_stalled_read_ends_the_download()
    {
        using var cts = new CancellationTokenSource();
        var stalled = new Stalled();
        var download = Download(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stalled) }), ct: cts.Token);
        await stalled.Reading.Task;

        cts.Cancel();

        Assert.Same(download, await Task.WhenAny(download, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download);
        Assert.False(File.Exists(_target));
    }

    [Fact]
    public async Task Cancelled_before_start_leaves_no_file()
    {
        File.WriteAllBytes(_target, new byte[] { 1 });
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Download(new StubHttpMessageHandler(_ => Archive()), ct: cts.Token));
        Assert.False(File.Exists(_target));
    }
}

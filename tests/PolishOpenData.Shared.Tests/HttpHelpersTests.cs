using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PolishOpenData.Internal;

namespace PolishOpenData.Shared.Tests;

public class HttpHelpersTests
{
    [Fact]
    public void User_agent_names_the_project()
    {
        Assert.StartsWith("PolishOpenData/", UserAgent.Value, StringComparison.Ordinal);
        Assert.EndsWith("(+https://github.com/JacekSmi/PolishOpenData)", UserAgent.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("+", UserAgent.Value.Split(' ')[0], StringComparison.Ordinal);   // no "+commit" metadata
    }

    [Fact]
    public void Get_request_carries_user_agent_and_accept()
    {
        using var request = HttpHelpers.CreateGet(new Uri("https://example.test/x"));
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(UserAgent.Value, string.Join(" ", request.Headers.GetValues("User-Agent")));
        Assert.Equal("application/json", request.Headers.GetValues("Accept").Single());
    }

    [Fact]
    public void Truncates_long_text()
    {
        Assert.Null(HttpHelpers.Truncate(null));
        Assert.Equal("abc", HttpHelpers.Truncate("abc"));
        Assert.Equal(512, HttpHelpers.Truncate(new string('x', 2000))!.Length);
    }

    [Fact]
    public void Snippet_is_the_start_of_the_utf8_body_without_a_byte_order_mark()
    {
        Assert.Equal(string.Empty, HttpHelpers.Snippet([]));
        Assert.Equal("{\"a\":1}", HttpHelpers.Snippet(Encoding.UTF8.GetBytes("\uFEFF{\"a\":1}")));
        Assert.Equal(new string('\u017C', 512), HttpHelpers.Snippet(Encoding.UTF8.GetBytes(new string('\u017C', 2000))));   // 2 bytes each
        Assert.Equal(new string('\u4E2D', 512), HttpHelpers.Snippet(Encoding.UTF8.GetBytes(new string('\u4E2D', 600))));    // 3 bytes each
        Assert.Equal("abc", HttpHelpers.Snippet(Encoding.UTF8.GetBytes("abcdef"), 3));
    }

    // The body of a success response is read after the headers (ResponseHeadersRead), where on .NET Framework neither
    // HttpClient.Timeout nor a resilience timeout applies: the caller's token is the only way to stop a slow body.
    [Fact]
    public async Task Reading_the_body_stops_between_chunks_when_cancelled()
    {
        using var body = new TrickleStream(Encoding.UTF8.GetBytes("{\"odpis\":"));
        using var content = new StreamContent(body);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        try
        {
            var read = content.ReadBytesAsync(cancellation.Token);
            var started = await Task.WhenAny(body.FirstChunkRead, read, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.True(ReferenceEquals(body.FirstChunkRead, started), "The first chunk was not read: " + read.Exception);
            cancellation.Cancel();

            var finished = await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.True(ReferenceEquals(read, finished), "The read did not stop within 10 s of the cancellation.");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        }
        finally
        {
            body.Release();   // ends a read that ignored the token, so no task is left waiting
        }
    }

    [Fact]
    public void Reads_retry_after_delta()
    {
        using var response = new HttpResponseMessage((HttpStatusCode)429);
        Assert.Null(HttpHelpers.GetRetryAfter(response));
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
        Assert.Equal(TimeSpan.FromSeconds(120), HttpHelpers.GetRetryAfter(response));
    }

    /// <summary>A body that sends one chunk and then waits, like a slow network, until cancelled or released.</summary>
    private sealed class TrickleStream(byte[] firstChunk) : Stream
    {
        private readonly TaskCompletionSource<bool> _firstChunkRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _reads;

        public Task FirstChunkRead => _firstChunkRead.Task;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public void Release() => _released.TrySetResult(true);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadCoreAsync(buffer, offset, count, cancellationToken);

#if NET
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var chunk = new byte[buffer.Length];
            var length = await ReadCoreAsync(chunk, 0, chunk.Length, cancellationToken).ConfigureAwait(false);
            chunk.AsMemory(0, length).CopyTo(buffer);
            return length;
        }
#endif

        // .NET Framework's HttpContent buffering reads through BeginRead, which calls Read: no token reaches it
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (TryReadFirstChunk(buffer, offset, count, out var length))
            {
                return length;
            }

            _released.Task.GetAwaiter().GetResult();
            return 0;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private async Task<int> ReadCoreAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (TryReadFirstChunk(buffer, offset, count, out var length))
            {
                return length;
            }

            var waiting = Task.Delay(Timeout.Infinite, cancellationToken);
            await Task.WhenAny(waiting, _released.Task).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return 0;
        }

        private bool TryReadFirstChunk(byte[] buffer, int offset, int count, out int length)
        {
            length = 0;
            if (Interlocked.Increment(ref _reads) != 1)
            {
                return false;
            }

            length = Math.Min(count, firstChunk.Length);
            Array.Copy(firstChunk, 0, buffer, offset, length);
            _firstChunkRead.TrySetResult(true);
            return true;
        }
    }
}

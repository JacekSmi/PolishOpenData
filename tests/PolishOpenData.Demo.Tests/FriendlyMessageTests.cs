using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PolishOpenData.Demo.State;

namespace PolishOpenData.Demo.Tests;

public class FriendlyMessageTests
{
    // Text no friendly message may contain; every exception below carries it in its message and its inner exception.
    private const string Detail = "SECRET-DETAIL-4711 https://uldk.gugik.gov.pl/?request=GetParcelByXY";

    public static TheoryData<Exception, string> Cases => new()
    {
        { new QuotaExceededException(Detail, HttpStatusCode.TooManyRequests, null, null, TimeSpan.FromSeconds(30)), "GUGiK is limiting requests right now. Wait a minute and try again." },
        { new QuotaExceededException(Detail), "GUGiK is limiting requests right now. Wait a minute and try again." },
        { new PolishOpenDataApiException(Detail, HttpStatusCode.OK, "blocked", "<html>The requested URL was rejected</html>", isTransient: true), "GUGiK's service refused the request. Try again in a few minutes." },
        { new PolishOpenDataApiException(Detail, HttpStatusCode.BadRequest, "uldk_error", Detail), "A GUGiK service answered with an error. Try again later." },
        { new PolishOpenDataApiException(Detail), "A GUGiK service answered with an error. Try again later." },
        { new HttpRequestException(Detail, new InvalidOperationException(Detail)), "A GUGiK service is not reachable right now. Check your connection and try again." },
        { new TaskCanceledException(Detail, new TimeoutException(Detail)), "A GUGiK service took too long to answer. Try again." },
        { new InvalidOperationException(Detail), "Something went wrong. Try again." },
        { new OperationCanceledException(Detail), "Something went wrong. Try again." },
        { new PolishOpenDataException(Detail), "Something went wrong. Try again." },
        { new ArgumentOutOfRangeException(Detail), "Something went wrong. Try again." },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Each_failure_has_its_sentence_and_never_the_exception_text(Exception exception, string expected)
    {
        var message = FriendlyMessage.For(exception);

        Assert.Equal(expected, message);
        Assert.DoesNotContain("SECRET-DETAIL", message, StringComparison.Ordinal);
        Assert.DoesNotContain(exception.Message, message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_timeout_is_told_from_a_cancelled_token_only_by_the_caller()
    {
        // The controller drops its own cancellation before asking for a message, so a TaskCanceledException that gets here is a timeout, whatever token it carries.
        using var source = new CancellationTokenSource();
        var exception = new TaskCanceledException("timeout", new TimeoutException(), source.Token);

        Assert.Equal("A GUGiK service took too long to answer. Try again.", FriendlyMessage.For(exception));
    }

    [Fact]
    public void The_messages_are_plain_sentences()
    {
        foreach (var message in new[]
        {
            FriendlyMessage.For(new QuotaExceededException()),
            FriendlyMessage.For(new PolishOpenDataApiException("x", HttpStatusCode.OK, "blocked", null)),
            FriendlyMessage.For(new PolishOpenDataApiException()),
            FriendlyMessage.For(new HttpRequestException()),
            FriendlyMessage.For(new TaskCanceledException()),
            FriendlyMessage.For(new InvalidOperationException()),
        })
        {
            Assert.EndsWith(".", message, StringComparison.Ordinal);
            Assert.True(message.Length < 100, message);
            Assert.DoesNotContain("Exception", message, StringComparison.Ordinal);
        }
    }
}

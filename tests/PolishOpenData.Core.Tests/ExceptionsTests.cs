using System;
using System.Net;

namespace PolishOpenData.Core.Tests;

public class ExceptionsTests
{
    [Fact]
    public void Api_exception_keeps_status_snippet_and_inner_exception()
    {
        var inner = new FormatException("bad date");
        var ex = new PolishOpenDataApiException("unreadable", HttpStatusCode.OK, "WL-0", "{\"x\":", inner, isTransient: true);

        Assert.Equal("unreadable", ex.Message);
        Assert.Equal(HttpStatusCode.OK, ex.StatusCode);
        Assert.Equal("WL-0", ex.ErrorCode);
        Assert.Equal("{\"x\":", ex.ResponseSnippet);
        Assert.Same(inner, ex.InnerException);
        Assert.True(ex.IsTransient);
    }
}

using System;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Uldk.Tests;

public class UldkResponseTests
{
    [Fact]
    public void Parse_zero_header_returns_pipe_split_rows()
    {
        var rows = UldkResponse.Parse(Fixture.Read("uldk/xy-warsaw.txt"));

        var row = Assert.Single(rows);
        Assert.Equal(8, row.Length);
        Assert.Equal("146510_8.0507.41/1", row[0]);
        Assert.Equal("mazowieckie", row[5]);
    }

    [Fact]
    public void Parse_not_found_with_stray_second_line_is_empty()
    {
        Assert.Empty(UldkResponse.Parse(Fixture.Read("uldk/not-found.txt")));
    }

    [Fact]
    public void Parse_count_header_returns_that_many_rows()
    {
        var rows = UldkResponse.Parse(Fixture.Read("uldk/count-krzewina-134.txt"));

        var row = Assert.Single(rows);
        Assert.Equal("022503_5.0003.134", row[0]);
        Assert.Equal("dolnośląskie", row[5]);
    }

    [Fact]
    public void Parse_minus_one_other_message_throws_uldk_error_with_the_message()
    {
        var exception = Assert.Throws<PolishOpenDataApiException>(() => UldkResponse.Parse("-1 niepoprawny parametr id\n"));

        Assert.Equal("uldk_error", exception.ErrorCode);
        Assert.Equal("niepoprawny parametr id", exception.Message);
    }

    [Fact]
    public void Parse_unrecognised_first_line_throws()
    {
        var exception = Assert.Throws<PolishOpenDataApiException>(() => UldkResponse.Parse("hello"));

        Assert.Equal("hello", exception.ResponseSnippet);
    }
}
